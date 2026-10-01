using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.PeakFrequency;

public sealed record PeakFrequencyResultPoint(double TimeSeconds, double StartSeconds, double EndSeconds,
    double? ValueHz, string State, string Quality, string Failure, double? PeakPowerV2PerHz)
{
    public bool IsComplete => State == "Complete" && ValueHz is not null;
}

public sealed record PeakFrequencyResultPreview(string Channel, PeakFrequencyResultPoint? StaticPoint,
    IReadOnlyList<PeakFrequencyResultPoint> DynamicPoints)
{
    public bool IsDynamic => DynamicPoints.Count > 0;
    public PeakFrequencyResultPoint? CurrentAt(double cursorSeconds) => IsDynamic
        ? DynamicPoints.LastOrDefault(point => point.EndSeconds <= cursorSeconds + 1e-9)
        : StaticPoint;
    public IReadOnlyList<PeakFrequencyResultPoint> VisibleThrough(double cursorSeconds) => IsDynamic
        ? DynamicPoints.Where(point => point.EndSeconds <= cursorSeconds + 1e-9).ToArray() : [];

    public static PeakFrequencyResultPreview? Parse(JsonElement? summary)
    {
        if (summary is not { ValueKind: JsonValueKind.Object } root ||
            !root.TryGetProperty("metric", out var metric) || metric.ValueKind != JsonValueKind.Object ||
            !metric.TryGetProperty("official", out var official) || Text(official, "algorithm_id") != "peak_frequency" ||
            !metric.TryGetProperty("output", out var output) || Text(output, "unit") != "Hz")
            return null;

        var channel = Text(metric, "channel");
        if (metric.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
        {
            var points = series.EnumerateArray().Select(ParseDynamicPoint)
                .Where(point => point is not null).Cast<PeakFrequencyResultPoint>().ToArray();
            return new PeakFrequencyResultPreview(channel, null, points);
        }

        var range = metric.TryGetProperty("actual_range", out var actual) ? actual : default;
        var value = Number(output, "value");
        var point = new PeakFrequencyResultPoint(double.NaN,
            Number(range, "start_s") ?? double.NaN, Number(range, "end_s") ?? double.NaN,
            value, value is null ? "Unavailable" : "Complete", Quality(metric), Failure(metric), PeakPower(official));
        return new PeakFrequencyResultPreview(channel, point, []);
    }

    private static PeakFrequencyResultPoint? ParseDynamicPoint(JsonElement point)
    {
        if (point.ValueKind != JsonValueKind.Object) return null;
        var time = Number(point, "time_s");
        var start = Number(point, "window_start_s");
        var end = Number(point, "window_end_s");
        if (time is null || start is null || end is null || end <= start) return null;
        var state = Text(point, "analysis_state");
        var official = point.TryGetProperty("official", out var evidence) ? evidence : default;
        return new PeakFrequencyResultPoint(time.Value, start.Value, end.Value,
            state == "Complete" ? Number(point, "value") : null,
            state, Quality(point), Failure(point), PeakPower(official));
    }

    private static double? PeakPower(JsonElement official) =>
        official.ValueKind == JsonValueKind.Object && official.TryGetProperty("peak_frequency_evidence", out var evidence)
            ? Number(evidence, "selected_power_v2_per_hz") : null;

    private static string Quality(JsonElement point) =>
        point.TryGetProperty("quality", out var quality) ? Text(quality, "status") : "";

    private static string Failure(JsonElement point)
    {
        if (point.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object)
            return Text(failure, "message") is { Length: > 0 } message ? message : Text(failure, "code");
        if (point.TryGetProperty("quality", out var quality) && quality.ValueKind == JsonValueKind.Object &&
            quality.TryGetProperty("reasons", out var reasons) && reasons.ValueKind == JsonValueKind.Array)
            return string.Join("、", reasons.EnumerateArray().Where(reason => reason.ValueKind == JsonValueKind.String)
                .Select(reason => reason.GetString()));
        return "";
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static double? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number : null;
}
