using System.Diagnostics;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed partial class RecordingReviewViewModel
{
    private long SpectralStepSamples => Math.Max(1, (long)Math.Round(
        PsdParameters.StepSeconds * samplingRateHz, MidpointRounding.AwayFromZero));

    private void PrepareUpcomingSpectralWindows(ReviewAnalysisCardViewModel slot)
    {
        if (!playback.IsPlaying || !slot.IsSpectralInspection || slot.IsClosed || isTimelineDragging ||
            isDisposing || slot.SpectralSourcesLoading || slot.Binding?.CurrentWindow is null ||
            slot.SpectralRbpBinding?.CurrentWindow is null) return;
        slot.SpectralPlaybackBuffer ??= new ReviewPlaybackBuffer<ReviewPreparedSpectralWindow>(
            (sample, token) => PrepareSpectralWindowAsync(slot, sample, token), analysisLifetime.Token);
        var sample = RecordingTimeMapper.ToCursorSample(PositionSeconds, samplingRateHz, playback.DurationSamples);
        slot.SpectralPlaybackBuffer.Advance(sample, SpectralStepSamples, playback.DurationSamples);
    }

    private async Task<ReviewPreparedSpectralWindow> PrepareSpectralWindowAsync(
        ReviewAnalysisCardViewModel slot, long sample, CancellationToken token)
    {
        var psdBinding = slot.Binding ?? throw new InvalidOperationException("PSD 未绑定。");
        var rbpBinding = slot.SpectralRbpBinding ?? throw new InvalidOperationException("RBP 未绑定。");
        var custom = slot.AlgorithmId == CustomFrequencyInspectionPanelId;
        var range = custom && CustomFrequencyInspection.Mode == ReviewCustomFrequencyMode.Range &&
            CustomFrequencyInspection.IsRangeValid;
        var low = CustomFrequencyInspection.RangeLowHz;
        var high = CustomFrequencyInspection.RangeHighHz;
        var started = Stopwatch.GetTimestamp();
        var psdTask = psdBinding.PrepareAtSampleAsync(sample, token);
        var rbpTask = rbpBinding.PrepareAtSampleAsync(sample, token);
        await Task.WhenAll(psdTask, rbpTask);
        token.ThrowIfCancellationRequested();
        var sourceMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var result = new ReviewPreparedSpectralWindow(await psdTask, await rbpTask);
        if (range) result = result with { RangeLowHz = low, RangeHighHz = high };
        try
        {
            ReviewSpectralSources.Validate(result.Psd, result.Rbp, PsdParameters.Channels);
            if (custom)
            {
                var provider = slot.Coordinator.GetCapability<IReviewCustomFrequencyProvider>() ??
                    throw new InvalidOperationException("PSD adapter 不提供自定义频率投影。");
                result = result with { Bins = await provider.LoadBinsAsync(result.Psd, result.Rbp, token) };
                if (range) result = result with
                {
                    Range = await provider.LoadRangeAsync(result.Psd, result.Rbp, low, high, token),
                };
            }
            else
            {
                var psdProvider = slot.Coordinator.GetCapability<IReviewScalarProvider>() ??
                    throw new InvalidOperationException("PSD adapter 不提供频段投影。");
                var rbpProvider = rbpBinding.Coordinator.GetCapability<IReviewScalarProvider>() ??
                    throw new InvalidOperationException("RBP adapter 不提供振幅投影。");
                var density = psdProvider.LoadScalarsAsync(result.Psd, token,
                    ReviewSpectralSources.Bands.Select(band => $"psd.band-mean-density.{band}").ToArray());
                var amplitude = rbpProvider.LoadScalarsAsync(result.Rbp, token,
                    ReviewSpectralSources.Bands.Select(band => $"rbp.amplitude.{band}").ToArray());
                await Task.WhenAll(density, amplitude);
                result = result with { Density = await density, Amplitude = await amplitude };
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { result = result with { ProjectionError = exception }; }
        token.ThrowIfCancellationRequested();
        if (DesktopLog.PerformanceDiagnosticsEnabled)
            DesktopLog.Info($"review_spectral_prepare_perf review_session_id={ReviewSessionId:N} recording_id={recordingId} " +
                $"target_cursor_sample={sample} source_ms={sourceMs:0.0} projection_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds - sourceMs:0.0} " +
                $"psd_window_id={result.Psd.Window?.WindowId} rbp_window_id={result.Rbp.Window?.WindowId} " +
                $"projection_state={(result.ProjectionError is null ? "ready" : "unavailable")} publication=false");
        return result;
    }

    private static ReviewPreparedSpectralWindow? CurrentPreparedSpectralWindow(
        ReviewAnalysisCardViewModel slot,
        BrainPlatform.Desktop.Modules.Algorithms.Contracts.WindowPreviewResponse psd,
        BrainPlatform.Desktop.Modules.Algorithms.Contracts.WindowPreviewResponse rbp) =>
        slot.PreparedSpectralWindow is { } prepared && ReferenceEquals(prepared.Psd, psd) &&
        ReferenceEquals(prepared.Rbp, rbp) ? prepared : null;

    private static void StopSpectralPreparation(ReviewAnalysisCardViewModel slot)
    {
        slot.SpectralPlaybackBuffer?.Dispose();
        slot.SpectralPlaybackBuffer = null;
    }
}
