using System.ComponentModel;
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
    private readonly int samplingRateHz;
    private readonly long durationSamples;
    private CancellationTokenSource? loadCancellation;
    private string? runId;
    private long lastRequestedSample = -1;
    private WindowPreviewResponse? currentWindow;
    private bool isLoading;
    private string statusText = "未绑定分析结果。";
    private int disposed;
    private long loadGeneration;

    public ReviewAnalysisBinding(
        ReviewAnalysisCoordinator coordinator,
        string recordingId,
        int samplingRateHz,
        long durationSamples)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.recordingId = string.IsNullOrWhiteSpace(recordingId)
            ? throw new ArgumentException("记录 ID 不能为空。", nameof(recordingId))
            : recordingId;
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

    public string? RunId => runId;

    public void AttachRun(string value)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Run ID 不能为空。", nameof(value));
        CancelPendingLoad();
        coordinator.Reset();
        Interlocked.Increment(ref loadGeneration);
        runId = value;
        lastRequestedSample = -1;
        CurrentWindow = null;
        StatusText = "已绑定 PSD 结果，等待游标。";
    }

    public void DetachRun()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        CancelPendingLoad();
        coordinator.Reset();
        Interlocked.Increment(ref loadGeneration);
        runId = null;
        lastRequestedSample = -1;
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
        if (runId is null)
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
        IsLoading = true;
        StatusText = "正在读取 PSD 窗口…";
        try
        {
            var result = await coordinator.LoadAtSampleAsync(recordingId, runId, sample, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (result is null || !IsCurrent(generation, linked)) return;
            CurrentWindow = result;
            StatusText = result.Status switch
            {
                "available" => "PSD 窗口已更新。",
                "unavailable" => "当前窗口不可用。",
                _ => $"PSD 窗口状态：{result.Status}",
            };
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsCurrent(generation, linked)) return;
            CurrentWindow = null;
            StatusText = $"PSD 结果读取失败：{exception.Message}";
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

    public void PreviewAtSeconds(double seconds)
    {
        if (Volatile.Read(ref disposed) != 0 || runId is null) return;
        if (!double.IsFinite(seconds)) return;
        var sample = RecordingTimeMapper.ToCursorSample(
            Math.Clamp(seconds, 0, durationSamples / (double)samplingRateHz), samplingRateHz, durationSamples);
        if (sample == lastRequestedSample) return;
        lastRequestedSample = sample;
        _ = LoadAtSampleAsync(sample);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return ValueTask.CompletedTask;
        CancelPendingLoad();
        coordinator.Reset();
        Interlocked.Increment(ref loadGeneration);
        runId = null;
        CurrentWindow = null;
        return ValueTask.CompletedTask;
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
