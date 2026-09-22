using BrainPlatform.Desktop.Configuration;
using System.Net.Http;

namespace BrainPlatform.Desktop.Review;

public interface IRecordingReviewReader
{
    LocalRawRecordingManifest Manifest { get; }
    double DurationSeconds { get; }
    Task<RecordingReviewWindow> ReadWindowAsync(double startSeconds, double durationSeconds, CancellationToken cancellationToken);
}

public sealed record RecordingReviewFrame(
    Guid RecordingSessionId, long Revision, int SamplingRateHz, long WindowStartSampleCounter,
    double WindowStartSeconds, double WindowEndSeconds, string ViewingMontageName,
    IReadOnlyList<ProjectedMontageSegment> Segments, IReadOnlyList<string> OutputChannelNames);

/// <summary>
/// Owns bounded review-window loading. A frame is a cache block; the viewport
/// moves continuously inside that block and therefore does not imply a reload.
/// </summary>
public sealed class RecordingReviewSession : IAsyncDisposable
{
    // Keep first paint quick at high sampling rates; the next bounded block is
    // loaded ahead of the visible range instead of making one giant request.
    private const double CacheCoverageMultiplier = 1.5;
    private const double FilteredSourceChunkSeconds = 10;
    private const double MinimumFilterWarmupSeconds = 5;
    private const double MaximumFilterWarmupSeconds = 30;
    private readonly IRecordingReviewReader reader;
    private readonly RecordingMontageCatalogResult catalog;
    private readonly IRecordingReviewFilter? filter;
    private readonly RecordingReviewWindowCache cache = new();
    private readonly ReviewFilteredSourceChunkCache filteredSourceCache = new();
    private readonly ReviewCheckpointCoordinator? checkpointCoordinator;
    private readonly object sourceChunkLoadGate = new();
    private readonly Dictionary<string, Task<ReviewFilteredSourceChunk>> sourceChunkLoads = [];
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? currentLoad;
    private long revision;
    private RecordingReviewFilterSettings filterSettings = new(1, 30, 50);
    private ReviewFilterContract? filterContract;
    private int disposed;

    public RecordingReviewSession(IRecordingReviewReader reader, RecordingMontageCatalogResult catalog,
        double visibleDurationSeconds, IRecordingReviewFilter? filter = null)
    {
        if (!double.IsFinite(visibleDurationSeconds) || visibleDurationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(visibleDurationSeconds));

        this.reader = reader;
        this.catalog = catalog;
        this.filter = filter;
        if (filter is not null)
        {
            checkpointCoordinator = new ReviewCheckpointCoordinator(
                reader, filter, new ReviewCheckpointAnchorCache());
            checkpointCoordinator.ProgressChanged += OnPreparationProgressChanged;
        }
        VisibleDurationSeconds = Math.Min(visibleDurationSeconds, reader.DurationSeconds);
        AcquisitionMontage = catalog.AcquisitionMontage;
        SelectedViewingMontage = catalog.AcquisitionMontage;
    }

    public event EventHandler? Changed;
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
            PositionSeconds = targetPosition;
            ViewportStartSeconds = targetViewportStart;
            CurrentFrame = cached;
            StatusText = DescribeFrame(cached, filterUnavailable);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        var requestRevision = Interlocked.Increment(ref revision);
        var load = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var previousLoad = Interlocked.Exchange(ref currentLoad, load);
        previousLoad?.Cancel();
        previousLoad?.Dispose();
        IsLoading = true;
        StatusText = "正在跳转到目标波形…";
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var result = await BuildFrameAsync(GetCacheStart(targetViewportStart), load.Token);
            load.Token.ThrowIfCancellationRequested();
            if (requestRevision != Volatile.Read(ref revision)) return false;

            var frame = result.Frame with { Revision = requestRevision };
            cache.StoreFrame(frame, filterSettings, SelectedViewingMontage, result.FilterUnavailable);
            PositionSeconds = targetPosition;
            ViewportStartSeconds = targetViewportStart;
            CurrentFrame = frame;
            StatusText = DescribeFrame(frame, result.FilterUnavailable);
            return true;
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested) { return false; }
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
                item => !string.Equals(item.Profile.Id, montage.Id, StringComparison.Ordinal)))
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

    private bool TryActivateCachedViewport()
    {
        if (!cache.TryGetFrame(ViewportStartSeconds, ViewportEndSeconds, filterSettings, SelectedViewingMontage,
                out var cached, out var filterUnavailable))
        {
            return false;
        }

        CurrentFrame = cached;
        StatusText = DescribeFrame(cached, filterUnavailable);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
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
            var result = await BuildFrameAsync(GetCacheStart(nextViewport), lifetime.Token);
            cache.StoreFrame(result.Frame, filterSettings, SelectedViewingMontage, result.FilterUnavailable);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch { /* Prefetch is opportunistic; foreground reads report errors. */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        currentLoad?.Cancel();
        currentLoad?.Dispose();
        lifetime.Dispose();
        if (checkpointCoordinator is not null)
        {
            checkpointCoordinator.ProgressChanged -= OnPreparationProgressChanged;
            await checkpointCoordinator.DisposeAsync();
        }
        await Task.CompletedTask;
    }

    private async Task LoadCacheBlockAsync(double cacheStartSeconds)
    {
        var requestRevision = Interlocked.Increment(ref revision);
        // Dragging is latest-only. The underlying source-chunk task remains
        // reusable, while this frame wait is cancelled so obsolete targets do
        // not hold the foreground worker or commit stale state.
        var nextLoad = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var previousLoad = Interlocked.Exchange(ref currentLoad, nextLoad);
        previousLoad?.Cancel();
        previousLoad?.Dispose();
        var load = currentLoad!;
        var token = load.Token;
        IsLoading = true;
        StatusText = "正在读取波形…";
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var result = await BuildFrameAsync(cacheStartSeconds, token);
            token.ThrowIfCancellationRequested();
            if (requestRevision != Volatile.Read(ref revision)) return;

            var frame = result.Frame with { Revision = requestRevision };
            cache.StoreFrame(frame, filterSettings, SelectedViewingMontage, result.FilterUnavailable);
            CurrentFrame = frame;
            StatusText = DescribeFrame(frame, result.FilterUnavailable);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
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

    private async Task<FrameBuildResult> BuildFrameAsync(double cacheStartSeconds, CancellationToken token)
    {
        var cacheEndSeconds = Math.Min(DurationSeconds, cacheStartSeconds + CacheDurationSeconds);
        var settings = filterSettings;
        var montage = SelectedViewingMontage;
        var sourceResult = await LoadDisplaySourceAsync(cacheStartSeconds, cacheEndSeconds, settings, token);
        var displayWindow = sourceResult.Window;

        var projected = MontageDisplayProjector.Project(displayWindow, reader.Manifest, montage);
        var outputNames = projected.FirstOrDefault()?.Channels.Select(channel => channel.Label).ToArray()
            ?? montage?.DerivedChannels.OrderBy(channel => channel.DisplayOrder).Select(channel => channel.Name).ToArray()
            ?? catalog.RawSignalView.ChannelLabels.ToArray();
        return new FrameBuildResult(new RecordingReviewFrame(reader.Manifest.SessionId, 0,
            reader.Manifest.SamplingRateHz, projected.FirstOrDefault()?.FirstSampleCounter ?? 0,
            displayWindow.ActualStartSeconds, displayWindow.ActualEndSeconds,
            montage?.Name ?? "原始设备通道", projected, outputNames), sourceResult.FilterUnavailable);
    }

    private async Task<SourceBuildResult> LoadDisplaySourceAsync(
        double startSeconds,
        double endSeconds,
        RecordingReviewFilterSettings settings,
        CancellationToken token)
    {
        if (filter is null)
        {
            return new SourceBuildResult(await ReadRawAsync(startSeconds, endSeconds, token), false);
        }

        ReviewFilterContract contract;
        try
        {
            contract = filterContract ??= await filter.GetContractAsync(token);
        }
        catch (HttpRequestException)
        {
            return new SourceBuildResult(await ReadRawAsync(startSeconds, endSeconds, token), true);
        }
        catch (TaskCanceledException) when (!token.IsCancellationRequested)
        {
            return new SourceBuildResult(await ReadRawAsync(startSeconds, endSeconds, token), true);
        }

        var firstChunk = (int)Math.Floor(startSeconds / FilteredSourceChunkSeconds);
        var lastChunk = (int)Math.Floor(Math.Max(startSeconds, endSeconds - 0.000_001) / FilteredSourceChunkSeconds);
        var chunks = new List<RecordingReviewWindow>(lastChunk - firstChunk + 1);
        for (var chunkIndex = firstChunk; chunkIndex <= lastChunk; chunkIndex++)
        {
            token.ThrowIfCancellationRequested();
            chunks.Add((await LoadFilteredSourceChunkAsync(chunkIndex, contract, settings, token)).Window);
        }

        return new SourceBuildResult(CombineChunks(startSeconds, endSeconds, chunks), false);
    }

    private async Task<ReviewFilteredSourceChunk> LoadFilteredSourceChunkAsync(
        int chunkIndex,
        ReviewFilterContract contract,
        RecordingReviewFilterSettings settings,
        CancellationToken token)
    {
        var startSeconds = chunkIndex * FilteredSourceChunkSeconds;
        var durationSeconds = Math.Min(FilteredSourceChunkSeconds, DurationSeconds - startSeconds);
        var sampleOffset = checked((long)Math.Floor(startSeconds * reader.Manifest.SamplingRateHz));
        var sampleCount = checked((int)Math.Ceiling(durationSeconds * reader.Manifest.SamplingRateHz));
        var usesExactCheckpointHistory = filter is HttpRecordingReviewFilter;
        var warmupSampleCount = usesExactCheckpointHistory ? 0 : checked((int)Math.Ceiling(
            Math.Min(startSeconds, GetFilterWarmupSeconds(settings)) * reader.Manifest.SamplingRateHz));
        var key = new ReviewFilteredSourceChunkKey(
            reader.Manifest.SessionId,
            ReviewFilteredSourceChunkKey.CreateManifestFingerprint(reader.Manifest),
            reader.Manifest.SamplingRateHz,
            ReviewFilteredSourceChunkKey.CreateChannelSchemaFingerprint(reader.Manifest),
            settings,
            contract,
            sampleOffset,
            sampleCount,
            warmupSampleCount,
            usesExactCheckpointHistory
                ? "recorded-segments-v2;exact-checkpoint-history;causal-state-never-crosses-gap"
                : "recorded-segments-v1;causal-state-never-crosses-gap");
        var work = GetOrStartFilteredSourceChunkBuild(key, chunkIndex, startSeconds, durationSeconds, settings, contract);
        return await work.WaitAsync(token);
    }

    private Task<ReviewFilteredSourceChunk> GetOrStartFilteredSourceChunkBuild(
        ReviewFilteredSourceChunkKey key,
        int chunkIndex,
        double startSeconds,
        double durationSeconds,
        RecordingReviewFilterSettings settings,
        ReviewFilterContract contract)
    {
        lock (sourceChunkLoadGate)
        {
            if (sourceChunkLoads.TryGetValue(key.Fingerprint, out var existing))
            {
                return existing;
            }

            var created = BuildFilteredSourceChunkAsync(key, chunkIndex, startSeconds, durationSeconds, settings, contract, lifetime.Token);
            sourceChunkLoads.Add(key.Fingerprint, created);
            _ = created.ContinueWith(completed =>
            {
                lock (sourceChunkLoadGate)
                {
                    if (sourceChunkLoads.TryGetValue(key.Fingerprint, out var current) && ReferenceEquals(current, completed))
                    {
                        sourceChunkLoads.Remove(key.Fingerprint);
                    }
                }
            }, TaskScheduler.Default);
            return created;
        }
    }

    private async Task<ReviewFilteredSourceChunk> BuildFilteredSourceChunkAsync(
        ReviewFilteredSourceChunkKey key,
        int chunkIndex,
        double startSeconds,
        double durationSeconds,
        RecordingReviewFilterSettings settings,
        ReviewFilterContract contract,
        CancellationToken token)
    {
        var cached = await filteredSourceCache.TryReadAsync(key, token);
        if (cached is not null)
        {
            return cached;
        }

        var raw = await ReadRawAsync(startSeconds, startSeconds + durationSeconds, token);
        if (raw.Segments.Count == 0)
        {
            return new ReviewFilteredSourceChunk(raw, null);
        }

        if (filter is not HttpRecordingReviewFilter)
        {
            var warmup = await ReadWarmupAsync(startSeconds, settings, token);
            var legacyFiltered = await filter!.ApplyWithWarmupAsync(
                warmup,
                raw,
                reader.Manifest,
                settings,
                token);
            await filteredSourceCache.StoreAsync(key, legacyFiltered, token);
            return new ReviewFilteredSourceChunk(legacyFiltered, null);
        }

        var coordinated = await checkpointCoordinator!.FilterAsync(raw, settings, contract, token)
            ?? throw new InvalidOperationException("HTTP 回溯滤波器没有提供 checkpoint 会话。");
        var filtered = coordinated.Window;
        token.ThrowIfCancellationRequested();
        await filteredSourceCache.StoreAsync(key, filtered, coordinated.Checkpoint, token);
        return new ReviewFilteredSourceChunk(filtered, coordinated.Checkpoint);
    }

    private async Task<RecordingReviewWindow> ReadRawAsync(double startSeconds, double endSeconds, CancellationToken token)
    {
        if (cache.TryGetRaw(startSeconds, endSeconds, out var cached))
        {
            return cached;
        }

        var window = await reader.ReadWindowAsync(startSeconds, endSeconds - startSeconds, token);
        cache.StoreRaw(window);
        return window;
    }

    private static RecordingReviewWindow CombineChunks(
        double requestedStartSeconds,
        double requestedEndSeconds,
        IReadOnlyList<RecordingReviewWindow> chunks)
    {
        var segments = chunks.SelectMany(chunk => chunk.Segments).ToArray();
        if (segments.Length == 0)
        {
            return new RecordingReviewWindow(requestedStartSeconds, requestedStartSeconds, requestedEndSeconds, []);
        }

        return new RecordingReviewWindow(
            requestedStartSeconds,
            chunks.First(chunk => chunk.Segments.Count > 0).ActualStartSeconds,
            chunks.Last(chunk => chunk.Segments.Count > 0).ActualEndSeconds,
            segments);
    }

    private async Task<RecordingReviewWindow?> ReadWarmupAsync(
        double cacheStartSeconds,
        RecordingReviewFilterSettings settings,
        CancellationToken token)
    {
        var duration = Math.Min(cacheStartSeconds, GetFilterWarmupSeconds(settings));
        if (duration <= 0)
        {
            return null;
        }

        var start = cacheStartSeconds - duration;
        if (cache.TryGetRaw(start, cacheStartSeconds, out var cached))
        {
            return cached;
        }

        var warmup = await reader.ReadWindowAsync(start, duration, token);
        cache.StoreRaw(warmup);
        return warmup;
    }

    private static double GetFilterWarmupSeconds(RecordingReviewFilterSettings settings) => Math.Clamp(
        3d / settings.HighPassHz,
        MinimumFilterWarmupSeconds,
        MaximumFilterWarmupSeconds);

    private static string DescribeFrame(RecordingReviewFrame frame, bool filterUnavailable) =>
        frame.Segments.Count == 0 ? "当前时间范围没有可显示的数据。"
        : filterUnavailable ? "滤波服务不可用，当前显示原始数据。"
        : frame.Segments.Count > 1 ? "记录存在采样缺口，已保留空白。"
        : "已加载";

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
    private sealed record FrameBuildResult(RecordingReviewFrame Frame, bool FilterUnavailable);
    private sealed record SourceBuildResult(RecordingReviewWindow Window, bool FilterUnavailable);
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(RecordingReviewSession));
    }
}
