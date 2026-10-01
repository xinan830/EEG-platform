using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.BandRatio;

public sealed record BandRatioEvidence(double? NumeratorLowHz, double? NumeratorHighHz, double? DenominatorLowHz, double? DenominatorHighHz, double? NumeratorPowerV2, double? DenominatorPowerV2, string Formula);
public sealed record BandRatioResultPoint(double TimeSeconds, double StartSeconds, double EndSeconds, double? Value, string State, string Quality, string Failure, BandRatioEvidence Evidence)
{
    public bool IsComplete => State == "Complete" && Value is not null;
}
public sealed record BandRatioResultPreview(string Channel, BandRatioResultPoint? StaticPoint, IReadOnlyList<BandRatioResultPoint> DynamicPoints)
{
    public bool IsDynamic => DynamicPoints.Count > 0;
    public BandRatioResultPoint? CurrentAt(double cursor) => IsDynamic ? DynamicPoints.LastOrDefault(point => point.EndSeconds <= cursor + 1e-9) : StaticPoint;
    public IReadOnlyList<BandRatioResultPoint> VisibleThrough(double cursor) => IsDynamic ? DynamicPoints.Where(point => point.EndSeconds <= cursor + 1e-9).ToArray() : [];
    public static BandRatioResultPreview? Parse(JsonElement? summary)
    {
        if (summary is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty("metric", out var metric) || metric.ValueKind != JsonValueKind.Object || !metric.TryGetProperty("official", out var official) || Text(official, "algorithm_id") != "band_ratio") return null;
        var channel = Text(metric, "channel");
        if (metric.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
            return new(channel, null, series.EnumerateArray().Select(ParsePoint).Where(point => point is not null).Cast<BandRatioResultPoint>().OrderBy(point => point.EndSeconds).ToArray());
        var range = metric.TryGetProperty("actual_range", out var actual) ? actual : default;
        var value = Number(metric.TryGetProperty("output", out var output) ? output : default, "value") ?? Number(metric, "value");
        return new(channel, new(double.NaN, Number(range, "start_s") ?? double.NaN, Number(range, "end_s") ?? double.NaN, value, value is null ? "Unavailable" : "Complete", Quality(metric), Failure(metric), Evidence(metric)), []);
    }
    private static BandRatioResultPoint? ParsePoint(JsonElement point)
    {
        var time = Number(point, "time_s"); var start = Number(point, "window_start_s"); var end = Number(point, "window_end_s");
        if (time is null || start is null || end is null || end <= start) return null;
        var state = Text(point, "analysis_state");
        return new(time.Value, start.Value, end.Value, state == "Complete" ? Number(point, "value") : null, state, Quality(point), Failure(point), Evidence(point));
    }
    private static BandRatioEvidence Evidence(JsonElement point)
    {
        var source = point.TryGetProperty("official", out var official) && official.ValueKind == JsonValueKind.Object ? official : point;
        var evidence = source.TryGetProperty("band_ratio_evidence", out var direct) && direct.ValueKind == JsonValueKind.Object ? direct : default;
        var trace = point.TryGetProperty("calculation_trace", out var traceValue) && traceValue.ValueKind == JsonValueKind.Object ? traceValue : default;
        return new(Range(evidence, "numerator_band_hz", 0), Range(evidence, "numerator_band_hz", 1), Range(evidence, "denominator_band_hz", 0), Range(evidence, "denominator_band_hz", 1), Number(evidence, "numerator_power_v2"), Number(evidence, "denominator_power_v2"), Text(trace, "formula"));
    }
    private static double? Range(JsonElement value, string key, int index) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var range) && range.ValueKind == JsonValueKind.Array && range.GetArrayLength() > index ? Number(range[index]) : null;
    private static string Quality(JsonElement value) => value.TryGetProperty("quality", out var quality) ? Text(quality, "status") : "";
    private static string Failure(JsonElement value) { if (value.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object) return Text(failure, "message") is { Length: > 0 } message ? message : Text(failure, "code"); return ""; }
    private static string Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
    private static double? Number(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) ? Number(item) : null;
    private static double? Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
}
