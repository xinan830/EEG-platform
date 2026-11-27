using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using System.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Publishes paired results at playback targets per spectral inspection card.</summary>
public sealed partial class RecordingReviewViewModel
{
    private Task RequestSpectralWindowAsync(ReviewAnalysisCardViewModel slot, double seconds,
        bool playbackAdvance = false, CancellationToken cancellationToken = default)
    {
        if (slot.IsClosed || slot.Binding?.IsOnDemandAttached != true || slot.SpectralRbpBinding?.IsOnDemandAttached != true)
            return Task.CompletedTask;
        var sample = RecordingTimeMapper.ToCursorSample(seconds, samplingRateHz, playback.DurationSamples);
        if (playbackAdvance)
        {
            var step = SpectralStepSamples;
            sample = sample / step * step;
            if (sample == slot.LastSpectralPlaybackSample) return Task.CompletedTask;
            slot.LastSpectralPlaybackSample = sample;
            if (slot.SpectralWindowLoad is { IsCompleted: false })
            {
                slot.SpectralPlaybackBuffer?.DiscardBefore(sample);
                slot.PendingSpectralPlaybackSample = sample;
                return Task.CompletedTask;
            }
        }
        else
        {
            CancelSpectralWindowLoad(slot);
            if (slot.AlgorithmId == SpectralValuesPanelId)
            {
                CancelSpectralProjection();
                SpectralValues.Clear("正在读取匹配的 PSD/RBP 窗口…");
            }
            if (slot.AlgorithmId == CustomFrequencyInspectionPanelId)
            {
                CancelCustomFrequencyProjection();
                CustomFrequencyInspection.Clear("正在读取匹配的 PSD/RBP 窗口…");
            }
            slot.LastSpectralPlaybackSample = sample;
        }
        var request = CancellationTokenSource.CreateLinkedTokenSource(analysisLifetime.Token, cancellationToken);
        var sourcesReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        slot.SpectralWindowCancellation = request;
        slot.SpectralWindowLoad = LoadSpectralWindowsAsync(slot, sample, request, sourcesReady);
        return sourcesReady.Task;
    }

    private async Task LoadSpectralWindowsAsync(ReviewAnalysisCardViewModel slot, long sample,
        CancellationTokenSource request, TaskCompletionSource sourcesReady)
    {
        try
        {
            while (true)
            {
                slot.PendingSpectralPlaybackSample = null;
                var psd = slot.Binding;
                var rbp = slot.SpectralRbpBinding;
                if (psd is null || rbp is null || slot.IsClosed) return;
                var started = Stopwatch.GetTimestamp();
                if (slot.AlgorithmId == CustomFrequencyInspectionPanelId && CustomFrequencyInspection.HasBins)
                    CustomFrequencyInspection.SetPendingWindow(sample / (double)samplingRateHz);
                var preparedTask = slot.SpectralPlaybackBuffer?.Take(sample);
                var bufferState = preparedTask is null ? "miss" : preparedTask.IsCompletedSuccessfully ? "ready" : "waiting";
                slot.SpectralSourcesLoading = true;
                if (slot.AlgorithmId == CustomFrequencyInspectionPanelId) customFrequencyBinsLoading = true;
                slot.PreparedSpectralWindow = null;
                ReviewPreparedSpectralWindow? preparedForPublication = null;
                if (preparedTask is not null)
                {
                    var prepared = await preparedTask.WaitAsync(request.Token);
                    request.Token.ThrowIfCancellationRequested();
                    await Task.WhenAll(psd.LoadPreparedAtSampleAsync(sample, prepared.Psd, request.Token),
                        rbp.LoadPreparedAtSampleAsync(sample, prepared.Rbp, request.Token));
                    preparedForPublication = prepared;
                }
                else
                {
                    StopSpectralPreparation(slot);
                    await Task.WhenAll(psd.LoadAtSampleAsync(sample, request.Token),
                        rbp.LoadAtSampleAsync(sample, request.Token));
                }
                request.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(psd, slot.Binding) || !ReferenceEquals(rbp, slot.SpectralRbpBinding) || slot.IsClosed) return;
                slot.PreparedSpectralWindow = preparedForPublication;
                var sourceMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                slot.SpectralSourcesLoading = false;
                sourcesReady.TrySetResult();
                if (slot.PendingSpectralPlaybackSample is null) PrepareUpcomingSpectralWindows(slot);
                if (slot.AlgorithmId == CustomFrequencyInspectionPanelId)
                    await RefreshCustomFrequencyInspectionAsync(slot);
                else if (slot.AlgorithmId == SpectralValuesPanelId)
                    await RefreshSpectralValuesAsync(slot);
                request.Token.ThrowIfCancellationRequested();
                if (DesktopLog.PerformanceDiagnosticsEnabled)
                    DesktopLog.Info($"review_spectral_pair_perf review_session_id={ReviewSessionId:N} recording_id={recordingId} " +
                        $"buffer_state={bufferState} target_cursor_sample={sample} psd_window_id={psd.CurrentWindow?.Window?.WindowId ?? "none"} " +
                        $"rbp_window_id={rbp.CurrentWindow?.Window?.WindowId ?? "none"} source_ms={sourceMs:0.0} " +
                        $"projection_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds - sourceMs:0.0} " +
                        $"playback_position_s={PositionSeconds:0.000} target_position_s={sample / (double)samplingRateHz:0.000} " +
                        $"psd_start_s={FormatPerformanceSeconds(psd.CurrentWindow?.WindowStartSeconds)} psd_end_s={FormatPerformanceSeconds(psd.CurrentWindow?.WindowEndSeconds)} " +
                        $"rbp_start_s={FormatPerformanceSeconds(rbp.CurrentWindow?.WindowStartSeconds)} rbp_end_s={FormatPerformanceSeconds(rbp.CurrentWindow?.WindowEndSeconds)} " +
                        $"psd_state={psd.CurrentWindow?.Status ?? "none"} rbp_state={rbp.CurrentWindow?.Status ?? "none"} " +
                        $"window_lag_s={FormatPerformanceSeconds(PositionSeconds - psd.CurrentWindow?.WindowEndSeconds)} " +
                        $"next_target_sample={slot.PendingSpectralPlaybackSample?.ToString() ?? "none"}");
                if (slot.PendingSpectralPlaybackSample is not { } next) return;
                sample = next;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!request.IsCancellationRequested && !slot.IsClosed)
            {
                if (slot.AlgorithmId == CustomFrequencyInspectionPanelId)
                    CustomFrequencyInspection.Clear($"窗口数值不可用：{exception.Message}");
                if (slot.AlgorithmId == SpectralValuesPanelId)
                    SpectralValues.Clear($"窗口数值不可用：{exception.Message}");
                DesktopLog.Error($"review_spectral_publication_failed review_session_id={ReviewSessionId:N} recording_id={recordingId}", exception);
            }
        }
        finally
        {
            sourcesReady.TrySetResult();
            if (ReferenceEquals(slot.SpectralWindowCancellation, request))
            {
                slot.SpectralWindowCancellation = null;
                slot.SpectralSourcesLoading = false;
            }
            request.Dispose();
        }
    }

    private static string FormatPerformanceSeconds(double? seconds) =>
        seconds?.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) ?? "none";

    private static void CancelSpectralWindowLoad(ReviewAnalysisCardViewModel slot)
    {
        StopSpectralPreparation(slot);
        slot.PreparedSpectralWindow = null;
        slot.SpectralSourcesLoading = false;
        slot.SpectralWindowCancellation?.Cancel();
        slot.SpectralWindowCancellation = null;
        slot.SpectralWindowLoad = null;
        slot.PendingSpectralPlaybackSample = null;
        slot.LastSpectralPlaybackSample = -1;
    }
}
