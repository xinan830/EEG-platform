namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

/// <summary>
/// Quantizes a recording-relative cursor for transport without planning an
/// analysis window. The backend remains the authority for the actual range.
/// </summary>
public static class DynamicWindowCursorQuantizer
{
    public static long QuantizeCursorSample(
        long cursorSample,
        int samplingRateHz,
        double refreshStepSeconds,
        double analysisStartSeconds = 0)
    {
        if (cursorSample < 0) throw new ArgumentOutOfRangeException(nameof(cursorSample));
        if (samplingRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(samplingRateHz));
        if (!double.IsFinite(refreshStepSeconds) || refreshStepSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(refreshStepSeconds));
        if (!double.IsFinite(analysisStartSeconds) || analysisStartSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(analysisStartSeconds));

        var stepSamplesValue = refreshStepSeconds * samplingRateHz;
        if (!double.IsFinite(stepSamplesValue) || stepSamplesValue < 1 || stepSamplesValue > long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(refreshStepSeconds));
        var stepSamples = checked((long)Math.Round(stepSamplesValue, MidpointRounding.AwayFromZero));
        if (stepSamples < 1) throw new ArgumentOutOfRangeException(nameof(refreshStepSeconds));

        var anchorValue = analysisStartSeconds * samplingRateHz;
        if (!double.IsFinite(anchorValue) || anchorValue >= 9223372036854775808d)
            throw new ArgumentOutOfRangeException(nameof(analysisStartSeconds));
        var anchorSample = checked((long)Math.Floor(anchorValue));

        // A cursor before the configured analysis range must not be moved
        // backwards past its own sample boundary.
        if (cursorSample <= anchorSample) return cursorSample;

        var offset = checked(cursorSample - anchorSample);
        return checked(anchorSample + offset / stepSamples * stepSamples);
    }
}
