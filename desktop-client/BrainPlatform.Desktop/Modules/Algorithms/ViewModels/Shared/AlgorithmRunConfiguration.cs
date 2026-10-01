using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

internal static class AlgorithmRunConfiguration
{
    internal sealed record FrequencyBand(double LowHz, double HighHz);
    internal sealed record BandRatioBands(FrequencyBand Numerator, FrequencyBand Denominator);

    internal static TimeRange ParseStaticRangeOrThrow(string startText, string endText, double durationSeconds)
    {
        if (!double.TryParse(startText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var start) ||
            !double.TryParse(endText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var end) ||
            !double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(durationSeconds) ||
            start < 0 || end <= start || end > durationSeconds)
        {
            throw new InvalidOperationException("请输入有效的分析范围，结束时间必须大于开始时间且不超过记录时长。");
        }

        return new TimeRange(start, end);
    }

    internal static FrequencyBand ParseFrequencyBandOrThrow(string lowText, string highText, double? samplingRateHz)
    {
        if (!double.TryParse(lowText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var low) ||
            !double.TryParse(highText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var high) ||
            !double.IsFinite(low) || !double.IsFinite(high) || low < 0 || high <= low)
        {
            throw new InvalidOperationException("请输入有效的频段范围，下限必须小于上限。");
        }

        if (samplingRateHz is not > 0 || !double.IsFinite(samplingRateHz.Value) || high >= samplingRateHz.Value / 2.0)
        {
            throw new InvalidOperationException("频段上限必须低于记录采样率的 Nyquist 频率。");
        }

        return new FrequencyBand(low, high);
    }

    internal static BandRatioBands ParseBandRatioBandsOrThrow(
        string numeratorLowText, string numeratorHighText,
        string denominatorLowText, string denominatorHighText,
        double? samplingRateHz)
    {
        var numerator = ParseFrequencyBandOrThrow(numeratorLowText, numeratorHighText, samplingRateHz);
        var denominator = ParseFrequencyBandOrThrow(denominatorLowText, denominatorHighText, samplingRateHz);
        return new BandRatioBands(numerator, denominator);
    }

    internal static JsonElement BuildStaticRunConfig(
        AlgorithmCatalogItem algorithm,
        string channel,
        TimeRange range,
        FrequencyBand? frequencyBand = null,
        BandRatioBands? ratioBands = null,
        string? f4Channel = null) => BuildRunConfig(algorithm, channel, range, "static", frequencyBand, ratioBands, f4Channel, null, null);

    internal static JsonElement BuildRunConfig(
        AlgorithmCatalogItem algorithm,
        string channel,
        TimeRange range,
        string mode,
        FrequencyBand? frequencyBand = null,
        BandRatioBands? ratioBands = null,
        string? f4Channel = null,
        double? windowSeconds = null,
        double? stepSeconds = null,
        double? notchHz = 50.0) =>
        JsonSerializer.SerializeToElement(BuildRunConfigPayload(algorithm, channel, range, mode, frequencyBand, ratioBands, f4Channel, windowSeconds, stepSeconds, notchHz));

    internal static bool IsSupportedAlgorithm(AlgorithmCatalogItem? algorithm) =>
        algorithm is { IsRunnable: true } &&
        AlgorithmRunResultHandlerRegistry.Contains(algorithm.Id) &&
        algorithm.Modes.Any();

    internal static bool IsSupportedStaticAlgorithm(AlgorithmCatalogItem? algorithm) =>
        IsSupportedAlgorithm(algorithm) && algorithm!.Modes.Contains("static");

    internal static double ParsePositiveSecondsOrThrow(string text, string label)
    {
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value <= 0)
            throw new InvalidOperationException($"请输入有效的{label}，必须大于 0。 ");
        return value;
    }

    internal static double ParseNotchFrequency(string text) =>
        string.Equals(text, "关闭", StringComparison.OrdinalIgnoreCase)
            ? 0.0
            : text.StartsWith("60", StringComparison.Ordinal)
                ? 60.0
                : 50.0;

    private static Dictionary<string, object?> BuildRunConfigPayload(
        AlgorithmCatalogItem algorithm,
        string channel,
        TimeRange range,
        string mode,
        FrequencyBand? frequencyBand,
        BandRatioBands? ratioBands,
        string? f4Channel,
        double? windowSeconds,
        double? stepSeconds,
        double? notchHz)
    {
        var payload = new Dictionary<string, object?>
        {
            ["algorithm_id"] = algorithm.Id,
            ["scientific_version"] = algorithm.Version,
            ["time"] = new { start_s = range.StartSeconds, end_s = range.EndSeconds },
            ["channel"] = channel,
            ["mode"] = mode,
            ["low_hz"] = frequencyBand?.LowHz,
            ["high_hz"] = frequencyBand?.HighHz,
            ["numerator_low_hz"] = ratioBands?.Numerator.LowHz,
            ["numerator_high_hz"] = ratioBands?.Numerator.HighHz,
            ["denominator_low_hz"] = ratioBands?.Denominator.LowHz,
            ["denominator_high_hz"] = ratioBands?.Denominator.HighHz,
            ["f4_channel"] = algorithm.Id == "faa" ? f4Channel : null,
        };

        if (algorithm.Id == "brainbeat")
            payload["secondary_channel"] = f4Channel;

        if (AlgorithmRunResultHandlerRegistry.Contains(algorithm.Id) &&
            AlgorithmRunResultHandlerRegistry.For(algorithm.Id).SupportsNotch)
            payload["notch_hz"] = notchHz;

        if (windowSeconds is double window && stepSeconds is double step)
        {
            payload["dynamic_window_s"] = window;
            payload["refresh_step_s"] = step;
        }

        return payload;
    }
}
