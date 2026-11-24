using System.Diagnostics;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed partial class RecordingReviewViewModel
{
    private ReviewPlaybackBuffer<ReviewPreparedSpectralWindow>? spectralPlaybackBuffer;
    private ReviewPreparedSpectralWindow? preparedSpectralWindow;
    private bool spectralSourcesLoading;

    private long SpectralStepSamples => Math.Max(1, (long)Math.Round(
        PsdParameters.StepSeconds * samplingRateHz, MidpointRounding.AwayFromZero));

    private void PrepareUpcomingSpectralWindows()
    {
        if (!playback.IsPlaying || !IsSpectralInspectionPanelOpen || isTimelineDragging ||
            isDisposing || spectralSourcesLoading || analysisBinding?.CurrentWindow is null ||
            spectralRbpBinding?.CurrentWindow is null) return;
        spectralPlaybackBuffer ??= new ReviewPlaybackBuffer<ReviewPreparedSpectralWindow>(PrepareSpectralWindowAsync, analysisLifetime.Token);
        var sample = RecordingTimeMapper.ToCursorSample(PositionSeconds, samplingRateHz, playback.DurationSamples);
        spectralPlaybackBuffer.Advance(sample, SpectralStepSamples, playback.DurationSamples);
    }

    private async Task<ReviewPreparedSpectralWindow> PrepareSpectralWindowAsync(long sample, CancellationToken token)
    {
        var psdBinding = analysisBinding ?? throw new InvalidOperationException("PSD 未绑定。");
        var rbpBinding = spectralRbpBinding ?? throw new InvalidOperationException("RBP 未绑定。");
        var custom = IsCustomFrequencyInspectionPanelOpen;
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
                var provider = analysisCoordinators["psd"].GetCapability<IReviewCustomFrequencyProvider>() ??
                    throw new InvalidOperationException("PSD adapter 不提供自定义频率投影。");
                result = result with { Bins = await provider.LoadBinsAsync(result.Psd, result.Rbp, token) };
                if (range) result = result with
                {
                    Range = await provider.LoadRangeAsync(result.Psd, result.Rbp, low, high, token),
                };
            }
            else
            {
                var psdProvider = analysisCoordinators["psd"].GetCapability<IReviewScalarProvider>() ??
                    throw new InvalidOperationException("PSD adapter 不提供频段投影。");
                var rbpProvider = analysisCoordinators["rbp"].GetCapability<IReviewScalarProvider>() ??
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

    private ReviewPreparedSpectralWindow? CurrentPreparedSpectralWindow(
        BrainPlatform.Desktop.Modules.Algorithms.Contracts.WindowPreviewResponse psd,
        BrainPlatform.Desktop.Modules.Algorithms.Contracts.WindowPreviewResponse rbp) =>
        preparedSpectralWindow is { } prepared && ReferenceEquals(prepared.Psd, psd) &&
        ReferenceEquals(prepared.Rbp, rbp) ? prepared : null;

    private void StopSpectralPreparation()
    {
        spectralPlaybackBuffer?.Dispose();
        spectralPlaybackBuffer = null;
    }
}
