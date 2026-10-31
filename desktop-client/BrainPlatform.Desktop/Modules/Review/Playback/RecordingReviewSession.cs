using System.Diagnostics;
using BrainPlatform.Desktop.Modules.Review.Diagnostics;
using BrainPlatform.Desktop.Modules.Review.Waveform;
using BrainPlatform.Desktop.Modules.Recordings.Access;
using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Modules.Review.Playback;

public sealed record RecordingReviewFrame(
    Guid RecordingSessionId, long Revision, int SamplingRateHz, long WindowStartSampleCounter,
    double WindowStartSeconds, double WindowEndSeconds, string ViewingMontageName,
    IReadOnlyList<ProjectedMontageSegment> Segments, IReadOnlyList<string> OutputChannelNames,
    string SourceRevision,
    IReadOnlyList<string>? OutputChannelColors = null,
    string? DisplaySourceIdentity = null);

/// <summary>
/// Owns bounded review-window loading. A frame is a cache block; the viewport
/// moves continuously inside that block and therefore does not imply a reload.
/// </summary>
public sealed class RecordingReviewSession : IAsyncDisposable
{
    // Keep first paint quick at high sampling rates; the next bounded block is
    // loaded ahead of the visible range instead of making one giant request.
    private const double CacheCoverageMultiplier = 1.5;
    private readonly IRecordingAccessSession reader;
    private readonly RecordingMontageCatalogResult catalog;
    private readonly RecordingReviewWindowCache cache = new();
    private readonly ReviewSourceLoader sourceLoader;
    private readonly ReviewDisplayLevelProvider displayLevelProvider = new();
    private readonly ReviewDisplayLevelPublisher displayLevelPublisher = new(
        new ReviewDisplayLevelChunkCache(), new ReviewDisplayLevelIndex());
    private readonly SemaphoreSlim displayLevelBuildGate = new(1, 1);
    private readonly IReviewPerformanceSink performanceSink;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? currentLoad;
    private long revision;
    private WaveformDisplayRequestContext currentDisplayRequest;
    private RecordingReviewFilterSettings filterSettings = new(1, 30, 50);
    private int disposed;

    public RecordingReviewSession(IRecordingAccessSession reader, RecordingMontageCatalogResult catalog,
        double visibleDurationSeconds, IRecordingReviewFilter? filter = null,
        IReviewPerformanceSink? performanceSink = null)
    {
        if (!double.IsFinite(visibleDurationSeconds) || visibleDurationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(visibleDurationSeconds));

        this.reader = reader;
        this.catalog = catalog;
        this.performanceSink = performanceSink ?? ReviewPerformanceDiagnostics.Default;
        sourceLoader = new ReviewSourceLoader(reader, filter, cache,
            (stage, source, window, elapsed) => RecordWindow(stage, source, window, null, elapsed));
        VisibleDurationSeconds = Math.Min(visibleDurationSeconds, reader.DurationSeconds);
        AcquisitionMontage = catalog.AcquisitionMontage;
        SelectedViewingMontage = catalog.AcquisitionMontage;
    }

    public event EventHandler? Changed;
    public Guid ReviewSessionId { get; } = Guid.NewGuid();
    public MontageProfile? AcquisitionMontage { get; }
    public MontageProfile? SelectedViewingMontage { get; private set; }
    public double PositionSeconds { get; private set; }
    public double ViewportStartSeconds { get; private set; }
    public double DurationSeconds => reader.DurationSeconds;
    public double VisibleDurationSeconds { get; private set; }
    public RecordingReviewFrame? CurrentFrame { get; private set; }
    public bool IsLoading { get; private set; }
    public string StatusText { get; private set; } = "等待读取记录。";
    public RecordingReviewFilterSettings FilterSettings => filterSettings;
    public ReviewPreparationState PreparationState { get; private set; } = ReviewPreparationState.Idle;

    /// <summary>
    /// Reads an optional completed display level for the chart. A miss is
    /// intentionally represented by null so the existing bounded frame path
    /// remains the authoritative fallback.
    /// </summary>
    internal async Task<ReviewDisplayFrame?> TryBuildDisplayFrameAsync(
        RecordingReviewFrame referenceFrame,
        double viewportStartSeconds,
        double visibleDurationSeconds,
        int plotWidthPixels,
        CancellationToken cancellationToken)
    {
        var display = await displayLevelProvider.TryReadAsync(referenceFrame, viewportStartSeconds,
            visibleDurationSeconds, plotWidthPixels, cancellationToken);
        if (display is not null || string.IsNullOrWhiteSpace(referenceFrame.DisplaySourceIdentity))
            return display;

        await displayLevelBuildGate.WaitAsync(cancellationToken);
        try
        {
            display = await displayLevelProvider.TryReadAsync(referenceFrame, viewportStartSeconds,
                visibleDurationSeconds, plotWidthPixels, cancellationToken);
            if (display is not null) return display;

            var level = ReviewDisplayLevelSelector.Select(
                visibleDurationSeconds, referenceFrame.SamplingRateHz, plotWidthPixels,
                referenceFrame.OutputChannelNames.Count, referenceFrame.DisplaySourceIdentity);
            var sampleCount = checked((int)Math.Ceiling(
                (referenceFrame.WindowEndSeconds - referenceFrame.WindowStartSeconds) * referenceFrame.SamplingRateHz));
            if (sampleCount <= 0) return null;

            async IAsyncEnumerable<ReviewDisplayLevelSourceChunk> Source()
            {
                yield return new ReviewDisplayLevelSourceChunk(
                    referenceFrame.DisplaySourceIdentity,
                    checked((int)Math.Max(0, referenceFrame.WindowStartSampleCounter / Math.Max(1, sampleCount))),
                    referenceFrame.WindowStartSampleCounter,
                    sampleCount,
                    referenceFrame.Segments);
                await Task.Yield();
            }

            await displayLevelPublisher.PublishAllChannelsFromSourceAsync(level, Source(), cancellationToken);
            return await displayLevelProvider.TryReadAsync(referenceFrame, viewportStartSeconds,
                visibleDurationSeconds, plotWidthPixels, cancellationToken);
        }
        finally
        {
            displayLevelBuildGate.Release();
        }
    }

    public Task InitializeAsync() => LoadViewportAsync(0, 0);

    // Preserves the old public contract where the seek value denoted a page start.
    public Task SeekAsync(double positionSeconds) => LoadViewportAsync(positionSeconds, positionSeconds);

    /// <summary>
    /// Track clicks are deliberate jumps. Keep the current frame and navigator
    /// position in place until the complete target frame is ready, then commit
    /// both together so the chart never visits an empty target range.
    /// </summary>
    public Task<bool> NavigateFromTrackClickAsync(double positionSeconds) =>
        NavigateFromTrackClickAsync(ClampViewportStart(positionSeconds), positionSeconds);

    public async Task<bool> NavigateFromTrackClickAsync(double viewportStartSeconds, double positionSeconds)
    {
        ThrowIfDisposed();
        var targetPosition = Math.Clamp(positionSeconds, 0, DurationSeconds);
        var targetViewportStart = ClampViewportStart(viewportStartSeconds);
        if (cache.TryGetFrame(targetViewportStart, Math.Min(DurationSeconds, targetViewportStart + VisibleDurationSeconds),
                filterSettings, SelectedViewingMontage, out var cached, out var filterUnavailable))
        {
            SupersedePendingLoad();
            PositionSeconds = targetPosition;
            ViewportStartSeconds = targetViewportStart;
            CurrentFrame = cached;
            StatusText = DescribeFrame(cached, filterUnavailable);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        var request = BeginDisplayRequest();
        var requestRevision = request.ViewGeneration;
        var load = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = load.Token;
        var previousLoad = Interlocked.Exchange(ref currentLoad, load);
        previousLoad?.Cancel();
        PreparationState = ReviewPreparationState.Idle;
        IsLoading = true;
        StatusText = "正在跳转到目标波形…";
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var started = Stopwatch.GetTimestamp();
            var result = await BuildFrameAsync(GetCacheStart(targetViewportStart), token,
                CreatePreparationProgress(requestRevision, token));
            token.ThrowIfCancellationRequested();
            if (requestRevision != Volatile.Read(ref revision)) return false;

            RecordFrameBuild(result, requestRevision, targetViewportStart, started);

            var frame = result.Frame with { Revision = requestRevision };
            return WaveformDisplayFramePublisher.TryPublish(
                new WaveformDisplayFrame<RecordingReviewFrame>(request, frame),
                currentDisplayRequest,
                token,
                payload =>
                {
                    cache.StoreFrame(payload, filterSettings, SelectedViewingMontage, result.FilterUnavailable);
                    PositionSeconds = targetPosition;
                    ViewportStartSeconds = targetViewportStart;
                    CurrentFrame = payload;
                    StatusText = DescribeFrame(payload, result.FilterUnavailable);
                });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception) when (requestRevision != Volatile.Read(ref revision)) { return false; }
        catch (Exception exception) when (requestRevision == Volatile.Read(ref revision))
        {
            StatusText = $"回溯跳转失败：{exception.Message}";
            return false;
        }
        finally
        {
            if (requestRevision == Volatile.Read(ref revision))
            {
                IsLoading = false;
                Changed?.Invoke(this, EventArgs.Empty);
            }
            Interlocked.CompareExchange(ref currentLoad, null, load);
            load.Dispose();
        }

    }

    public Task LoadViewportAsync(double viewportStartSeconds, double positionSeconds)
    {
        ThrowIfDisposed();
        PositionSeconds = Math.Clamp(positionSeconds, 0, DurationSeconds);
        ViewportStartSeconds = ClampViewportStart(viewportStartSeconds);
        RecordNavigationTarget(ViewportStartSeconds);
        if (TryActivateCachedViewport())
        {
            return Task.CompletedTask;
        }

        return LoadCacheBlockAsync(GetCacheStart(ViewportStartSeconds));
    }

    public Task SetVisibleDurationAsync(double visibleDurationSeconds, double? positionSeconds = null)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(visibleDurationSeconds) || visibleDurationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(visibleDurationSeconds));

        VisibleDurationSeconds = Math.Min(visibleDurationSeconds, reader.DurationSeconds);
        return LoadViewportAsync(ViewportStartSeconds, positionSeconds ?? PositionSeconds);
    }

    public Task SelectViewingMontageAsync(MontageProfile? montage, double? positionSeconds = null)
    {
        ThrowIfDisposed();
        if (montage is not null && catalog.CompatibleViewingMontages.All(
                item => !ReferenceEquals(item.Profile, montage)))
            throw new InvalidOperationException("所选导联与此记录的原始通道不兼容。");

        SelectedViewingMontage = montage;
        return LoadViewportAsync(ViewportStartSeconds, positionSeconds ?? PositionSeconds);
    }

    public Task SetFilterAsync(RecordingReviewFilterSettings settings, double? positionSeconds = null)
    {
        ThrowIfDisposed();
        settings.Validate(reader.Manifest.SamplingRateHz);
        filterSettings = settings;
        return LoadViewportAsync(ViewportStartSeconds, positionSeconds ?? PositionSeconds);
    }

    public bool ContainsViewport(double viewportStartSeconds)
    {
        var start = ClampViewportStart(viewportStartSeconds);
        return CurrentFrame is { } frame && frame.WindowStartSeconds <= start + 0.000_001 &&
               frame.WindowEndSeconds + 0.000_001 >= Math.Min(DurationSeconds, start + VisibleDurationSeconds);
    }

    /// <summary>
    /// Promotes an already-prefetched block without disk, Python, or chart-data
    /// work. The playback clock calls this as soon as it crosses a block edge.
    /// </summary>
    public bool TryActivateCachedViewport(double viewportStartSeconds, double positionSeconds)
    {
        ThrowIfDisposed();
        PositionSeconds = Math.Clamp(positionSeconds, 0, DurationSeconds);
        ViewportStartSeconds = ClampViewportStart(viewportStartSeconds);
        return TryActivateCachedViewport();
    }

    /// <summary>Fences the previous user target before a throttled replacement starts.</summary>
    public bool PreviewNavigationTarget(double viewportStartSeconds, double positionSeconds)
    {
        ThrowIfDisposed();
        SupersedePendingLoad();
        return TryActivateCachedViewport(viewportStartSeconds, positionSeconds);
    }

    private bool TryActivateCachedViewport()
    {
        if (!cache.TryGetFrame(ViewportStartSeconds, ViewportEndSeconds, filterSettings, SelectedViewingMontage,
                out var cached, out var filterUnavailable))
        {
            return false;
        }

        SupersedePendingLoad();
        CurrentFrame = cached;
        StatusText = DescribeFrame(cached, filterUnavailable);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void SupersedePendingLoad()
    {
        Interlocked.Increment(ref revision);
        currentDisplayRequest = default;
        var obsolete = Interlocked.Exchange(ref currentLoad, null);
        obsolete?.Cancel();
        PreparationState = ReviewPreparationState.Idle;
        IsLoading = false;
    }

    public async Task PrefetchNextAsync()
    {
        // Speculative work only runs while no foreground target is active.
        if (currentLoad is not null)
            return;
        if (CurrentFrame is not { } frame || frame.WindowEndSeconds >= DurationSeconds - 0.000_001)
            return;

        var nextViewport = ClampViewportStart(frame.WindowEndSeconds - VisibleDurationSeconds * 0.1);
        if (cache.TryGetFrame(nextViewport, Math.Min(DurationSeconds, nextViewport + VisibleDurationSeconds),
                filterSettings, SelectedViewingMontage, out _, out _))
            return;

        try
        {
            var settings = filterSettings;
            var montage = SelectedViewingMontage;
            var result = await BuildFrameAsync(GetCacheStart(nextViewport), lifetime.Token);
            cache.StoreFrame(result.Frame, settings, montage, result.FilterUnavailable);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch { /* Prefetch is opportunistic; foreground reads report errors. */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        SupersedePendingLoad();
        lifetime.Cancel();
        lifetime.Dispose();
        await sourceLoader.DisposeAsync();
        displayLevelBuildGate.Dispose();
    }

    private async Task LoadCacheBlockAsync(double cacheStartSeconds)
    {
        var request = BeginDisplayRequest();
        var requestRevision = request.ViewGeneration;
        // Dragging is latest-only. The underlying source-chunk task remains
        // reusable, while this frame wait is cancelled so obsolete targets do
        // not hold the foreground worker or commit stale state.
        var nextLoad = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var previousLoad = Interlocked.Exchange(ref currentLoad, nextLoad);
        previousLoad?.Cancel();
        var load = nextLoad;
        var token = load.Token;
        PreparationState = ReviewPreparationState.Idle;
        IsLoading = true;
        StatusText = "正在读取波形…";
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var started = Stopwatch.GetTimestamp();
            var result = await BuildFrameAsync(cacheStartSeconds, token,
                CreatePreparationProgress(requestRevision, token));
            token.ThrowIfCancellationRequested();
            if (requestRevision != Volatile.Read(ref revision)) return;

            RecordFrameBuild(result, requestRevision, ViewportStartSeconds, started);

            var frame = result.Frame with { Revision = requestRevision };
            WaveformDisplayFramePublisher.TryPublish(
                new WaveformDisplayFrame<RecordingReviewFrame>(request, frame),
                currentDisplayRequest,
                token,
                payload =>
                {
                    cache.StoreFrame(payload, filterSettings, SelectedViewingMontage, result.FilterUnavailable);
                    CurrentFrame = payload;
                    StatusText = DescribeFrame(payload, result.FilterUnavailable);
                });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception) when (requestRevision != Volatile.Read(ref revision)) { return; }
        catch (Exception exception) when (requestRevision == Volatile.Read(ref revision))
        {
            StatusText = $"回溯读取失败：{exception.Message}";
        }
        finally
        {
            if (requestRevision == Volatile.Read(ref revision))
            {
                IsLoading = false;
                Changed?.Invoke(this, EventArgs.Empty);
            }
            Interlocked.CompareExchange(ref currentLoad, null, load);
            load.Dispose();
        }
    }

    private WaveformDisplayRequestContext BeginDisplayRequest()
    {
        var metadata = reader.Metadata;
        var request = new WaveformDisplayRequestContext(
            metadata.RecordingId,
            metadata.Revision,
            Interlocked.Increment(ref revision),
            Guid.NewGuid());
        currentDisplayRequest = request;
        return request;
    }

    private async Task<FrameBuildResult> BuildFrameAsync(double cacheStartSeconds, CancellationToken token,
        Action<ReviewPreparationState>? progress = null)
    {
        var cacheEndSeconds = Math.Min(DurationSeconds, cacheStartSeconds + CacheDurationSeconds);
        var settings = filterSettings;
        var montage = SelectedViewingMontage;
        var sourceResult = await sourceLoader.LoadAsync(cacheStartSeconds, cacheEndSeconds, settings, token, progress);
        var displayWindow = sourceResult.Window;

        var projected = SynchronizationContext.Current is null
            ? MontageDisplayProjector.Project(displayWindow, reader.Manifest, montage)
            : await Task.Run(() => MontageDisplayProjector.Project(displayWindow, reader.Manifest, montage), token);
        var outputNames = projected.FirstOrDefault()?.Channels.Select(channel => channel.Label).ToArray()
            ?? montage?.DerivedChannels.OrderBy(channel => channel.DisplayOrder).Select(channel => channel.Name).ToArray()
            ?? catalog.RawSignalView.ChannelLabels.ToArray();
        return new FrameBuildResult(new RecordingReviewFrame(reader.Manifest.SessionId, 0,
            reader.Manifest.SamplingRateHz, projected.FirstOrDefault()?.FirstSampleCounter ?? 0,
            displayWindow.ActualStartSeconds, displayWindow.ActualEndSeconds,
            montage?.Name ?? "原始设备通道", projected, outputNames,
            reader.Metadata.Revision,
            MontageOutputAppearance.ResolveColors(montage, outputNames),
            sourceResult.FilterUnavailable || sourceResult.FilterContract is null
                ? ReviewDisplaySourceIdentity.CreateRaw(reader.Manifest, montage)
                : ReviewDisplaySourceIdentity.CreateFiltered(reader.Manifest, montage,
                    settings, sourceResult.FilterContract)),
            sourceResult.FilterUnavailable, sourceResult.Source);
    }

    private static string DescribeFrame(RecordingReviewFrame frame, bool filterUnavailable) =>
        frame.Segments.Count == 0 ? "当前时间范围没有可显示的数据。"
        : filterUnavailable ? "滤波服务不可用，当前显示原始数据。"
        : frame.Segments.Count > 1 ? "记录存在采样缺口，已保留空白。"
        : "已加载";

    private Action<ReviewPreparationState> CreatePreparationProgress(long requestRevision, CancellationToken token)
    {
        var context = SynchronizationContext.Current;
        void Publish(ReviewPreparationState state)
        {
            if (Volatile.Read(ref disposed) != 0 || token.IsCancellationRequested ||
                requestRevision != Volatile.Read(ref revision) || !IsLoading) return;
            OnPreparationProgressChanged(null, state);
        }
        return state =>
        {
            if (context is null || ReferenceEquals(context, SynchronizationContext.Current)) Publish(state);
            else context.Post(_ => Publish(state), null);
        };
    }

    private void OnPreparationProgressChanged(object? sender, ReviewPreparationState state)
    {
        PreparationState = state;
        StatusText = state.Phase switch
        {
            ReviewPreparationPhase.PreparingHistory when state.Progress is { } progress =>
                $"正在准备精确滤波历史… {progress:P0}",
            ReviewPreparationPhase.PreparingHistory => "正在准备精确滤波历史…",
            ReviewPreparationPhase.FilteringTarget => "正在生成目标滤波波形…",
            ReviewPreparationPhase.Failed => $"精确滤波准备失败：{state.Failure}",
            _ => StatusText,
        };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private double ViewportEndSeconds => Math.Min(DurationSeconds, ViewportStartSeconds + VisibleDurationSeconds);
    private double CacheDurationSeconds => Math.Min(DurationSeconds,
        Math.Max(VisibleDurationSeconds, VisibleDurationSeconds * CacheCoverageMultiplier));
    private double GetCacheStart(double viewportStartSeconds)
    {
        var spare = Math.Max(0, CacheDurationSeconds - VisibleDurationSeconds);
        return Math.Clamp(viewportStartSeconds - spare * 0.25, 0, Math.Max(0, DurationSeconds - CacheDurationSeconds));
    }
    private double ClampViewportStart(double value) => Math.Clamp(value, 0, Math.Max(0, DurationSeconds - VisibleDurationSeconds));
    private void RecordFrameBuild(FrameBuildResult result, long revision, double requestedStartSeconds, long started)
    {
        performanceSink.Record(new ReviewPerformanceSample(
            ReviewSessionId,
            reader.Manifest.SessionId,
            ReviewPerformanceStage.FrameBuild,
            result.Source,
            revision,
            requestedStartSeconds,
            requestedStartSeconds + VisibleDurationSeconds,
            result.Frame.OutputChannelNames.Count,
            0,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds));
    }

    private void RecordWindow(
        ReviewPerformanceStage stage,
        ReviewPerformanceSource source,
        RecordingReviewWindow window,
        long? revision,
        double elapsedMilliseconds)
    {
        var segment = window.Segments.FirstOrDefault();
        performanceSink.Record(new ReviewPerformanceSample(
            ReviewSessionId,
            reader.Manifest.SessionId,
            stage,
            source,
            revision,
            window.ActualStartSeconds,
            window.ActualEndSeconds,
            segment?.ChannelCount ?? 0,
            window.Segments.Sum(item => item.SampleCount),
            elapsedMilliseconds));
    }

    private void RecordNavigationTarget(double viewportStartSeconds) =>
        performanceSink.Record(new ReviewPerformanceSample(
            ReviewSessionId,
            reader.Manifest.SessionId,
            ReviewPerformanceStage.NavigationTarget,
            ReviewPerformanceSource.InteractiveTarget,
            revision,
            viewportStartSeconds,
            Math.Min(DurationSeconds, viewportStartSeconds + VisibleDurationSeconds),
            0,
            0,
            0));

    private sealed record FrameBuildResult(
        RecordingReviewFrame Frame,
        bool FilterUnavailable,
        ReviewPerformanceSource Source);
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(RecordingReviewSession));
    }
}
