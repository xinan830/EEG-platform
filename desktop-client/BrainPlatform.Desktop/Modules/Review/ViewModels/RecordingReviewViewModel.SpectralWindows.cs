using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using System.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Owns one paired source/projection update and one latest playback target.</summary>
public sealed partial class RecordingReviewViewModel
{
    private CancellationTokenSource? spectralWindowCancellation;
    private Task? spectralWindowLoad;
    private long? pendingSpectralPlaybackSample;
    private long lastSpectralPlaybackSample = -1;

    private Task RequestSpectralWindowAsync(double seconds, bool playbackAdvance = false,
        CancellationToken cancellationToken = default)
    {
        if (analysisBinding?.IsOnDemandAttached != true || spectralRbpBinding?.IsOnDemandAttached != true)
            return Task.CompletedTask;
        var sample = RecordingTimeMapper.ToCursorSample(seconds, samplingRateHz, playback.DurationSamples);
        if (playbackAdvance)
        {
            var step = Math.Max(1, (long)Math.Round(PsdParameters.StepSeconds * samplingRateHz,
                MidpointRounding.AwayFromZero));
            sample = sample / step * step;
            if (sample == lastSpectralPlaybackSample) return Task.CompletedTask;
            lastSpectralPlaybackSample = sample;
            if (spectralWindowLoad is { IsCompleted: false })
            {
                pendingSpectralPlaybackSample = sample;
                return Task.CompletedTask;
            }
        }
        else
        {
            CancelSpectralWindowLoad();
            CancelSpectralProjection();
            CancelCustomFrequencyProjection();
            SpectralValues.Clear("正在读取匹配的 PSD/RBP 窗口…");
            CustomFrequencyInspection.Clear("正在读取匹配的 PSD/RBP 窗口…");
            lastSpectralPlaybackSample = sample;
        }
        var request = CancellationTokenSource.CreateLinkedTokenSource(analysisLifetime.Token, cancellationToken);
        var sourcesReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        spectralWindowCancellation = request;
        spectralWindowLoad = LoadSpectralWindowsAsync(sample, request, sourcesReady);
        return sourcesReady.Task;
    }

    private async Task LoadSpectralWindowsAsync(long sample, CancellationTokenSource request, TaskCompletionSource sourcesReady)
    {
        try
        {
            while (true)
            {
                pendingSpectralPlaybackSample = null;
                var psd = analysisBinding;
                var rbp = spectralRbpBinding;
                if (psd is null || rbp is null) return;
                var started = Stopwatch.GetTimestamp();
                if (CustomFrequencyInspection.HasBins)
                    CustomFrequencyInspection.SetPendingWindow(sample / (double)samplingRateHz);
                await Task.WhenAll(psd.LoadAtSampleAsync(sample, request.Token),
                    rbp.LoadAtSampleAsync(sample, request.Token));
                request.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(psd, analysisBinding) || !ReferenceEquals(rbp, spectralRbpBinding)) return;
                var sourceMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                sourcesReady.TrySetResult();
                // Backend windows are step-aligned; requested cursor equality is not a pairing contract.
                if (IsCustomFrequencyInspectionPanelOpen) await RefreshCustomFrequencyInspectionAsync();
                if (IsSpectralValuesPanelOpen) await RefreshSpectralValuesAsync();
                request.Token.ThrowIfCancellationRequested();
                if (DesktopLog.PerformanceDiagnosticsEnabled)
                    DesktopLog.Info($"review_spectral_pair_perf review_session_id={ReviewSessionId:N} recording_id={recordingId} " +
                        $"target_cursor_sample={sample} psd_window_id={psd.CurrentWindow?.Window?.WindowId ?? "none"} " +
                        $"rbp_window_id={rbp.CurrentWindow?.Window?.WindowId ?? "none"} source_ms={sourceMs:0.0} " +
                        $"projection_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds - sourceMs:0.0} " +
                        $"playback_position_s={PositionSeconds:0.000} target_position_s={sample / (double)samplingRateHz:0.000} " +
                        $"psd_start_s={FormatPerformanceSeconds(psd.CurrentWindow?.WindowStartSeconds)} psd_end_s={FormatPerformanceSeconds(psd.CurrentWindow?.WindowEndSeconds)} " +
                        $"rbp_start_s={FormatPerformanceSeconds(rbp.CurrentWindow?.WindowStartSeconds)} rbp_end_s={FormatPerformanceSeconds(rbp.CurrentWindow?.WindowEndSeconds)} " +
                        $"psd_state={psd.CurrentWindow?.Status ?? "none"} rbp_state={rbp.CurrentWindow?.Status ?? "none"} " +
                        $"window_lag_s={FormatPerformanceSeconds(PositionSeconds - psd.CurrentWindow?.WindowEndSeconds)} " +
                        $"next_target_sample={pendingSpectralPlaybackSample?.ToString() ?? "none"}");
                if (pendingSpectralPlaybackSample is not { } next) return;
                sample = next;
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            sourcesReady.TrySetResult();
            if (ReferenceEquals(spectralWindowCancellation, request)) spectralWindowCancellation = null;
            request.Dispose();
        }
    }

    private static string FormatPerformanceSeconds(double? seconds) =>
        seconds?.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) ?? "none";

    private void CancelSpectralWindowLoad()
    {
        spectralWindowCancellation?.Cancel();
        spectralWindowCancellation = null;
        spectralWindowLoad = null;
        pendingSpectralPlaybackSample = null;
        lastSpectralPlaybackSample = -1;
    }
}
