using System.ComponentModel;
using System.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>
/// Binds one completed Run to the review clock without creating another clock.
/// It coalesces cursor movement and never lets an old response overwrite a new one.
/// </summary>
public sealed class ReviewAnalysisBinding : INotifyPropertyChanged, IAsyncDisposable
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

    public async Task LoadAtSampleAsync(long sample, CancellationToken cancellationToken = default)
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
            var result = onDemandSnapshot is { } snapshot
                ? await coordinator.LoadOnDemandAtSampleAsync(recordingId,
                    snapshot with { CursorSample = sample }, sample, linked.Token, progress)
                : await coordinator.LoadAtSampleAsync(recordingId, runId!, sample, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (result is null || !IsCurrent(generation, linked)) return;
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
            }
        }
    }

    public void OnPlaybackCursorAdvanced(double seconds, long generation = 0)
    {
        if (Volatile.Read(ref disposed) != 0 || (runId is null && onDemandSnapshot is null)) return;
        if (!double.IsFinite(seconds)) return;
        if (!AcceptPlaybackGeneration(generation)) return;
        var sample = RecordingTimeMapper.ToCursorSample(
            Math.Clamp(seconds, 0, durationSamples / (double)samplingRateHz), samplingRateHz, durationSamples);
        var refreshStep = onDemandRefreshStepSamples ?? samplingRateHz;
        sample = sample / refreshStep * refreshStep;
        RequestPreviewAtSample(sample);
    }

    public void OnCursorPositionSelected(double seconds, long generation = 0)
    {
        if (!AcceptPlaybackGeneration(generation)) return;
        RequestPreviewAtSeconds(seconds);
    }

    public void OnCursorDragCompleted(double seconds, long generation = 0)
    {
        if (Volatile.Read(ref disposed) != 0 || (runId is null && onDemandSnapshot is null)) return;
        if (!double.IsFinite(seconds)) return;
        if (!AcceptPlaybackGeneration(generation)) return;
        var sample = RecordingTimeMapper.ToCursorSample(
            Math.Clamp(seconds, 0, durationSamples / (double)samplingRateHz), samplingRateHz, durationSamples);
        RequestPreviewAtSample(sample);
    }

    private void RequestPreviewAtSeconds(double seconds)
    {
        if (Volatile.Read(ref disposed) != 0 || (runId is null && onDemandSnapshot is null)) return;
        if (!double.IsFinite(seconds)) return;
        var sample = RecordingTimeMapper.ToCursorSample(
            Math.Clamp(seconds, 0, durationSamples / (double)samplingRateHz), samplingRateHz, durationSamples);
        sample = QuantizeOnDemandPreviewSample(sample);
        RequestPreviewAtSample(sample);
    }

    private void RequestPreviewAtSample(long sample)
    {
        if (sample == lastRequestedSample) return;
        lastRequestedSample = sample;
        _ = LoadAtSampleAsync(sample);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        CancelPendingLoad();
        coordinator.Reset();
        Interlocked.Increment(ref loadGeneration);
        runId = null;
        onDemandSnapshot = null;
        onDemandRefreshStepSamples = null;
        CurrentWindow = null;
        await coordinator.ReleaseOnDemandAsync();
    }

    private long QuantizeOnDemandPreviewSample(long sample)
    {
        if (onDemandRefreshStepSamples is not > 0) return sample;
        var step = onDemandRefreshStepSamples.Value;
        return sample / step * step;
    }

    private long? ReadRefreshStepSamples(System.Text.Json.JsonElement config)
    {
        if (!config.TryGetProperty("refresh_step_s", out var stepElement) ||
            stepElement.ValueKind != System.Text.Json.JsonValueKind.Number ||
            !stepElement.TryGetDouble(out var stepSeconds) ||
            !double.IsFinite(stepSeconds) || stepSeconds <= 0)
        {
            return null;
        }

        var samples = stepSeconds * samplingRateHz;
        if (!double.IsFinite(samples) || samples < 1 || samples > long.MaxValue)
            return null;
        return Math.Max(1, (long)Math.Round(samples, MidpointRounding.AwayFromZero));
    }

    private void CancelPendingLoad()
    {
        loadCancellation?.Cancel();
        loadCancellation = null;
    }

    private bool AcceptPlaybackGeneration(long generation)
    {
        if (generation < playbackGeneration) return false;
        if (generation == playbackGeneration) return true;
        playbackGeneration = generation;
        lastRequestedSample = -1;
        return true;
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
