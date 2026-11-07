using System.ComponentModel;
using System.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>
/// Binds one completed Run to the review clock without creating another clock.
/// It coalesces cursor movement and never lets an old response overwrite a new one.
/// </summary>
public sealed partial class ReviewAnalysisBinding : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ReviewAnalysisCoordinator coordinator;
    private readonly string recordingId;
    private readonly RegisteredRecording? registeredRecording;
    private readonly int samplingRateHz;
    private readonly long durationSamples;
    private CancellationTokenSource? loadCancellation;
    private string? runId;
    private ReviewAnalysisSnapshot? onDemandSnapshot;
    private long lastRequestedSample = -1;
    private WindowPreviewResponse? currentWindow;
    private bool isLoading;
    private string statusText = "未绑定分析结果。";
    private int disposed;
    private long loadGeneration;
    private long playbackGeneration;
    private long targetCursorSample = -1;
    private long? onDemandRefreshStepSamples;

    public ReviewAnalysisBinding(
        ReviewAnalysisCoordinator coordinator,
        string recordingId,
        int samplingRateHz,
        long durationSamples,
        RegisteredRecording? registeredRecording = null)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.recordingId = string.IsNullOrWhiteSpace(recordingId)
            ? throw new ArgumentException("记录 ID 不能为空。", nameof(recordingId))
            : recordingId;
        if (registeredRecording is not null && !string.Equals(registeredRecording.Id, this.recordingId, StringComparison.Ordinal))
            throw new ArgumentException("注册记录与回溯记录身份不匹配。", nameof(registeredRecording));
        this.registeredRecording = registeredRecording;
        if (samplingRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(samplingRateHz));
        if (durationSamples < 0) throw new ArgumentOutOfRangeException(nameof(durationSamples));
        this.samplingRateHz = samplingRateHz;
        this.durationSamples = durationSamples;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WindowPreviewResponse? CurrentWindow
    {
        get => currentWindow;
        private set => Set(ref currentWindow, value, nameof(CurrentWindow));
    }

    public bool IsLoading
    {
        get => isLoading;
        private set => Set(ref isLoading, value, nameof(IsLoading));
    }

    public string StatusText
    {
        get => statusText;
        private set => Set(ref statusText, value, nameof(StatusText));
    }

    /// <summary>Latest playback target. It may be newer than the displayed window.</summary>
    public long TargetCursorSample
    {
        get => targetCursorSample;
        private set => Set(ref targetCursorSample, value, nameof(TargetCursorSample));
    }

    public double TargetCursorSeconds => TargetCursorSample < 0
        ? 0
        : TargetCursorSample / (double)samplingRateHz;

    /// <summary>Cursor returned by the backend for the currently displayed window.</summary>
    public long? DisplayedCursorSample => CurrentWindow?.CursorSample;

    public double? DisplayedCursorSeconds => CurrentWindow?.CursorSeconds;

    public string DisplayedWindowStatus => CurrentWindow?.Status ?? "none";

    public IReadOnlyList<string> DisplayedWindowReasonCodes =>
        CurrentWindow?.ReasonCodes ?? Array.Empty<string>();

    public bool IsTargetDisplayed => CurrentWindow is not null &&
        CurrentWindow.CursorSample == TargetCursorSample && !IsLoading;

    public string? RunId => runId;

    public bool IsOnDemandAttached => onDemandSnapshot is not null;

    public void AttachRun(string value)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Run ID 不能为空。", nameof(value));
        SetPlaybackActive(false, playbackGeneration);
        CancelPendingLoad();
        coordinator.Reset();
        Interlocked.Increment(ref loadGeneration);
        runId = value;
        onDemandSnapshot = null;
        onDemandRefreshStepSamples = null;
        lastRequestedSample = -1;
        playbackGeneration = 0;
        TargetCursorSample = -1;
        CurrentWindow = null;
        StatusText = $"已绑定 {coordinator.AlgorithmDisplayName} 结果，等待游标。";
    }

    public void AttachOnDemand(AnalysisRunRequest request, AlgorithmCatalogItem? algorithm = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        SetPlaybackActive(false, playbackGeneration);
        CancelPendingLoad();
        coordinator.Reset();
        Interlocked.Increment(ref loadGeneration);
        runId = null;
        onDemandSnapshot = ReviewAnalysisSnapshot.From(request,
            registeredRecording ?? throw new InvalidOperationException("按需分析必须绑定已注册记录身份。"),
            0, algorithm);
        onDemandRefreshStepSamples = ReadRefreshStepSamples(onDemandSnapshot.Configuration);
        lastRequestedSample = -1;
        playbackGeneration = 0;
        TargetCursorSample = -1;
        CurrentWindow = null;
        StatusText = $"已绑定按需 {coordinator.AlgorithmDisplayName}，等待游标。";
    }

    public void DetachRun()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        SetPlaybackActive(false, playbackGeneration);
        CancelPendingLoad();
        coordinator.Reset();
        Interlocked.Increment(ref loadGeneration);
        runId = null;
        onDemandSnapshot = null;
        onDemandRefreshStepSamples = null;
        lastRequestedSample = -1;
        playbackGeneration = 0;
        TargetCursorSample = -1;
        CurrentWindow = null;
        IsLoading = false;
        StatusText = "未绑定分析结果。";
    }

    public Task LoadAtSecondsAsync(double seconds, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > durationSamples / (double)samplingRateHz)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        var sample = RecordingTimeMapper.ToCursorSample(seconds, samplingRateHz, durationSamples);
        return LoadAtSampleAsync(sample, cancellationToken);
    }

    public Task LoadAtSampleAsync(long sample, CancellationToken cancellationToken = default)
    {
        ResetPlaybackPreparation();
        return LoadWindowAsync(sample, null, cancellationToken);
    }

    internal async Task<WindowPreviewResponse> PrepareAtSampleAsync(long sample, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (sample < 0 || sample > durationSamples) throw new ArgumentOutOfRangeException(nameof(sample));
        var snapshot = onDemandSnapshot ?? throw new InvalidOperationException("未绑定按需窗口。");
        var result = await coordinator.PrepareOnDemandAtSampleAsync(snapshot with { CursorSample = sample }, sample, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(snapshot, onDemandSnapshot) || Volatile.Read(ref disposed) != 0)
            throw new OperationCanceledException("分析绑定已改变。");
        return result;
    }

    internal Task LoadPreparedAtSampleAsync(long sample, WindowPreviewResponse prepared, CancellationToken cancellationToken) =>
        LoadWindowAsync(sample, prepared, cancellationToken);

    private async Task LoadWindowAsync(long sample, WindowPreviewResponse? prepared, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (runId is null && onDemandSnapshot is null)
        {
            StatusText = "未绑定分析结果。";
            return;
        }
        if (sample < 0 || sample > durationSamples)
            throw new ArgumentOutOfRangeException(nameof(sample));

        CancelPendingLoad();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        loadCancellation = linked;
        var generation = Interlocked.Increment(ref loadGeneration);
        var loadStarted = Stopwatch.GetTimestamp();
        TargetCursorSample = sample;
        IsLoading = true;
        StatusText = $"正在读取 {coordinator.AlgorithmDisplayName} 窗口…";
        try
        {
            var progress = new Progress<DynamicWindowResponse>(status =>
            {
                if (!IsCurrent(generation, linked) || !IsLoading) return;
                StatusText = status.State switch
                {
                    "preparing_source" => "正在准备连续预处理源…",
                    "computing_window" => $"正在计算当前 {coordinator.AlgorithmDisplayName} 窗口…",
                    "queued" => $"当前 {coordinator.AlgorithmDisplayName} 窗口等待计算…",
                    _ => StatusText,
                };
            });
            linked.Token.ThrowIfCancellationRequested();
            var resultTask = prepared is not null && onDemandSnapshot is { } preparedSnapshot
                ? Task.FromResult<WindowPreviewResponse?>(coordinator.AdoptPreparedWindow(
                    preparedSnapshot with { CursorSample = sample }, prepared))
                : onDemandSnapshot is { } snapshot
                    ? coordinator.LoadOnDemandAtSampleAsync(recordingId,
                        snapshot with { CursorSample = sample }, sample, linked.Token, progress)
                    : coordinator.LoadAtSampleAsync(recordingId, runId!, sample, linked.Token);
            var (result, completedAt) = await AlgorithmWindowPerformanceDiagnostics.WithCompletionTimeAsync(resultTask);
            var continuationMs = Stopwatch.GetElapsedTime(completedAt).TotalMilliseconds;
            linked.Token.ThrowIfCancellationRequested();
            if (result is null || !IsCurrent(generation, linked)) return;
            var bindStarted = Stopwatch.GetTimestamp();
            CurrentWindow = result;
            AlgorithmWindowPerformanceDiagnostics.RecordResultBound(
                recordingId, coordinator.AlgorithmId, result.Window?.WindowId, sample,
                AlgorithmWindowPerformanceDiagnostics.ElapsedMilliseconds(loadStarted));
            StatusText = result.Status switch
            {
                "available" => $"{coordinator.AlgorithmDisplayName} 窗口已更新。",
                "insufficient_data" => "当前窗口数据不足。",
                "rejected" => $"当前 {coordinator.AlgorithmDisplayName} 窗口被质量规则拒绝。",
                "cancelled" => $"当前 {coordinator.AlgorithmDisplayName} 窗口已取消。",
                "failed" => $"当前 {coordinator.AlgorithmDisplayName} 窗口计算失败。",
                "unavailable" => $"当前 {coordinator.AlgorithmDisplayName} 窗口不可用。",
                _ => $"{coordinator.AlgorithmDisplayName} 窗口状态：{result.Status}",
            };
            if (AlgorithmWindowPerformanceDiagnostics.Enabled)
                DesktopLog.Info($"review_analysis_binding_perf recording_id={recordingId} algorithm_id={coordinator.AlgorithmId}" +
                    $" window_id={result.Window?.WindowId ?? "none"} cursor_sample={sample} state={result.Status}" +
                    $" continuation_delay_ms={continuationMs:0.000} binding_ms={Stopwatch.GetElapsedTime(bindStarted).TotalMilliseconds:0.000}");
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsCurrent(generation, linked)) return;
            CurrentWindow = null;
            StatusText = $"{coordinator.AlgorithmDisplayName} 结果读取失败：{exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(loadCancellation, linked))
            {
                loadCancellation = null;
                IsLoading = false;
                PrepareUpcomingScalarWindows();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        SetPlaybackActive(false, playbackGeneration);
        CancelPendingLoad();
        coordinator.Reset();
        Interlocked.Increment(ref loadGeneration);
        runId = null;
        onDemandSnapshot = null;
        onDemandRefreshStepSamples = null;
        CurrentWindow = null;
        await coordinator.ReleaseOnDemandAsync();
    }

    private void CancelPendingLoad()
    {
        loadCancellation?.Cancel();
        loadCancellation = null;
    }

    private void Set<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName is nameof(CurrentWindow) or nameof(IsLoading))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayedCursorSample)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayedCursorSeconds)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayedWindowStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayedWindowReasonCodes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTargetDisplayed)));
        }
        if (propertyName == nameof(TargetCursorSample))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetCursorSeconds)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTargetDisplayed)));
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(ReviewAnalysisBinding));
    }

    private bool IsCurrent(long generation, CancellationTokenSource request) =>
        Volatile.Read(ref disposed) == 0 &&
        generation == Volatile.Read(ref loadGeneration) &&
        ReferenceEquals(loadCancellation, request);
}
