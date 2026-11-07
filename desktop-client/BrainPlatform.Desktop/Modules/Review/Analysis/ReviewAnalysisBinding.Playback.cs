using System.Diagnostics;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

public sealed partial class ReviewAnalysisBinding
{
    private ReviewPlaybackBuffer<WindowPreviewResponse>? scalarPlaybackBuffer;
    private CancellationTokenSource? scalarPlaybackCancellation;
    private long? pendingScalarPlaybackSample;
    private long latestPlaybackSample;
    private bool playbackPreparationEnabled;

    private bool CanPrepareScalarPlayback => onDemandSnapshot is not null &&
        coordinator.AlgorithmId is "faa" or "iapf";

    /// <summary>Playback owns preparation lifetime; manual previews retain their existing path.</summary>
    public void SetPlaybackActive(bool active, long generation = 0)
    {
        if (!AcceptPlaybackGeneration(generation)) return;
        active &= CanPrepareScalarPlayback && Volatile.Read(ref disposed) == 0;
        if (playbackPreparationEnabled == active) return;
        playbackPreparationEnabled = active;
        if (!active) ResetPlaybackPreparation();
        else
        {
            if (CurrentWindow is not null && !IsLoading)
                lastRequestedSample = QuantizeOnDemandPreviewSample(CurrentWindow.CursorSample);
            PrepareUpcomingScalarWindows();
        }
    }

    private void PrepareUpcomingScalarWindows()
    {
        if (!playbackPreparationEnabled || !CanPrepareScalarPlayback || IsLoading || CurrentWindow is null) return;
        scalarPlaybackBuffer ??= new ReviewPlaybackBuffer<WindowPreviewResponse>(
            PrepareScalarWindowAsync, CancellationToken.None);
        scalarPlaybackBuffer.Advance(Math.Max(0, TargetCursorSample),
            onDemandRefreshStepSamples ?? samplingRateHz, durationSamples);
    }

    private async Task<WindowPreviewResponse> PrepareScalarWindowAsync(long sample, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await PrepareAtSampleAsync(sample, token);
        if (DesktopLog.PerformanceDiagnosticsEnabled)
            DesktopLog.Info($"review_scalar_prepare_perf recording_id={recordingId} algorithm_id={coordinator.AlgorithmId} " +
                $"target_cursor_sample={sample} window_id={result.Window?.WindowId} " +
                $"preparation_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.000} publication=false");
        return result;
    }

    private void RequestScalarPlayback(long sample)
    {
        if (sample == lastRequestedSample) return;
        lastRequestedSample = sample;
        TargetCursorSample = sample;
        if (scalarPlaybackCancellation is not null)
        {
            pendingScalarPlaybackSample = sample;
            scalarPlaybackBuffer?.DiscardBefore(sample);
            return;
        }
        var request = new CancellationTokenSource();
        scalarPlaybackCancellation = request;
        _ = LoadScalarPlaybackAsync(sample, request);
    }

    private async Task LoadScalarPlaybackAsync(long sample, CancellationTokenSource request)
    {
        var token = request.Token;
        try
        {
            while (true)
            {
                pendingScalarPlaybackSample = null;
                IsLoading = true;
                StatusText = $"正在读取 {coordinator.AlgorithmDisplayName} 窗口…";
                var started = Stopwatch.GetTimestamp();
                var prepared = scalarPlaybackBuffer?.Take(sample);
                var bufferState = prepared is null ? "miss" : prepared.IsCompletedSuccessfully ? "ready" : "waiting";
                if (prepared is null)
                {
                    scalarPlaybackBuffer?.Dispose();
                    scalarPlaybackBuffer = null;
                    prepared = PrepareScalarWindowAsync(sample, token);
                }
                var result = await prepared.WaitAsync(token);
                token.ThrowIfCancellationRequested();
                // Under overload, finish the active read but publish only the newest playback target.
                var publish = pendingScalarPlaybackSample is null;
                if (publish) await LoadPreparedAtSampleAsync(sample, result, token);
                if (DesktopLog.PerformanceDiagnosticsEnabled)
                    DesktopLog.Info($"review_scalar_playback_perf recording_id={recordingId} algorithm_id={coordinator.AlgorithmId} " +
                        $"target_cursor_sample={sample} window_id={result.Window?.WindowId} buffer_state={bufferState} " +
                        $"publication={publish} wait_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.000} " +
                        $"playback_position_s={latestPlaybackSample / (double)samplingRateHz:0.000} " +
                        $"window_end_s={result.WindowEndSeconds:0.000} " +
                        $"window_lag_s={latestPlaybackSample / (double)samplingRateHz - result.WindowEndSeconds:0.000}");
                if (pendingScalarPlaybackSample is not { } next) return;
                sample = next;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!ReferenceEquals(scalarPlaybackCancellation, request)) return;
            CurrentWindow = null;
            StatusText = $"{coordinator.AlgorithmDisplayName} 结果读取失败：{exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(scalarPlaybackCancellation, request))
            {
                scalarPlaybackCancellation = null;
                IsLoading = false;
            }
            request.Dispose();
        }
    }

    private void ResetPlaybackPreparation()
    {
        scalarPlaybackBuffer?.Dispose();
        scalarPlaybackBuffer = null;
        var active = scalarPlaybackCancellation;
        scalarPlaybackCancellation = null;
        active?.Cancel();
        pendingScalarPlaybackSample = null;
        lastRequestedSample = -1;
        if (active is not null) IsLoading = false;
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
        latestPlaybackSample = RecordingTimeMapper.ToCursorSample(
            Math.Clamp(seconds, 0, durationSamples / (double)samplingRateHz), samplingRateHz, durationSamples);
        if (CanPrepareScalarPlayback && playbackPreparationEnabled)
            RequestScalarPlayback(sample);
        else RequestPreviewAtSample(sample);
    }

    public void OnCursorPositionSelected(double seconds, long generation = 0)
    {
        if (!AcceptPlaybackGeneration(generation)) return;
        ResetPlaybackPreparation();
        RequestPreviewAtSeconds(seconds);
    }

    public void OnCursorDragCompleted(double seconds, long generation = 0)
    {
        if (Volatile.Read(ref disposed) != 0 || (runId is null && onDemandSnapshot is null)) return;
        if (!double.IsFinite(seconds)) return;
        if (!AcceptPlaybackGeneration(generation)) return;
        ResetPlaybackPreparation();
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
        _ = LoadWindowAsync(sample, null, CancellationToken.None);
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

    private bool AcceptPlaybackGeneration(long generation)
    {
        if (generation < playbackGeneration) return false;
        if (generation == playbackGeneration) return true;
        ResetPlaybackPreparation();
        playbackGeneration = generation;
        lastRequestedSample = -1;
        return true;
    }

}
