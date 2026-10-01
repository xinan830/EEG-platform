using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Iapf;

public sealed record IapfResultPoint(
    double TimeSeconds,
    double StartSeconds,
    double EndSeconds,
    double? ValueHz,
    string State,
    string Quality,
    string Failure,
    string Source,
    double? PeakHz,
    double? CogHz,
    double? ModelR2)
{
    public bool IsComplete => State == "Complete" && ValueHz is not null;
}

public sealed record IapfResultPreview(
    string Channel,
    IapfResultPoint? StaticPoint,
    IReadOnlyList<IapfResultPoint> DynamicPoints)
{
    public bool IsDynamic => DynamicPoints.Count > 0;

    public IapfResultPoint? CurrentAt(double cursorSeconds) => IsDynamic
        ? DynamicPoints.LastOrDefault(point => point.EndSeconds <= cursorSeconds + 1e-9)
        : StaticPoint;

    public IReadOnlyList<IapfResultPoint> VisibleThrough(double cursorSeconds) => IsDynamic
        ? DynamicPoints.Where(point => point.EndSeconds <= cursorSeconds + 1e-9).ToArray()
        : [];

    public static IapfResultPreview? Parse(JsonElement? summary)
    {
        if (summary is not { ValueKind: JsonValueKind.Object } root ||
            !root.TryGetProperty("metric", out var metric) || metric.ValueKind != JsonValueKind.Object ||
            !metric.TryGetProperty("official", out var official) ||
            Text(official, "algorithm_id") != "iapf" ||
            !metric.TryGetProperty("output", out var output) || Text(output, "unit") != "Hz")
            return null;

        var channel = Text(metric, "channel");
        if (metric.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
        {
            var points = series.EnumerateArray().Select(ParseDynamicPoint).Where(point => point is not null).Cast<IapfResultPoint>().ToArray();
            return new IapfResultPreview(channel, null, points);
        }

        var evidence = Evidence(metric);
        var range = metric.TryGetProperty("actual_range", out var actual) ? actual : default;
        var valueHz = Number(output, "value");
        var point = new IapfResultPoint(
            double.NaN, Number(range, "start_s") ?? double.NaN, Number(range, "end_s") ?? double.NaN,
            valueHz, valueHz is null ? "Unavailable" : "Complete", Quality(metric), Failure(metric),
            Text(evidence, "source"), Number(evidence, "peak_hz"), Number(evidence, "cog_hz"), Number(evidence, "model_r2"));
        return new IapfResultPreview(channel, point, []);
    }

    private static IapfResultPoint? ParseDynamicPoint(JsonElement point)
    {
        if (point.ValueKind != JsonValueKind.Object) return null;
        var time = Number(point, "time_s");
        var start = Number(point, "window_start_s");
        var end = Number(point, "window_end_s");
        if (time is null || start is null || end is null || end <= start) return null;
        var evidence = Evidence(point);
        var state = Text(point, "analysis_state");
        return new IapfResultPoint(time.Value, start.Value, end.Value,
            state == "Complete" ? Number(point, "value") : null,
            state, Quality(point), Failure(point), Text(evidence, "source"),
            Number(evidence, "peak_hz"), Number(evidence, "cog_hz"), Number(evidence, "model_r2"));
    }

    private static JsonElement Evidence(JsonElement point) =>
        point.TryGetProperty("official", out var official) && official.ValueKind == JsonValueKind.Object &&
        official.TryGetProperty("iapf_evidence", out var evidence) && evidence.ValueKind == JsonValueKind.Object
            ? evidence : default;

    private static string Quality(JsonElement point) =>
        point.TryGetProperty("quality", out var quality) ? Text(quality, "status") : "";

    private static string Failure(JsonElement point) =>
        point.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object
            ? Text(failure, "message") is { Length: > 0 } message ? message : Text(failure, "code")
            : "";

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static double? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number : null;
}
