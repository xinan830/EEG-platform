using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.ThetaBeta;

public sealed record ThetaBetaEvidence(
    double? IapfHz,
    double? ThetaLowHz,
    double? ThetaHighHz,
    double? BetaLowHz,
    double? BetaHighHz,
    double? ThetaPowerUv2,
    double? BetaPowerUv2,
    string Formula);

public sealed record ThetaBetaResultPoint(
    double TimeSeconds,
    double StartSeconds,
    double EndSeconds,
    double? Value,
    string State,
    string Quality,
    string Failure,
    ThetaBetaEvidence Evidence)
{
    public bool IsComplete => State == "Complete" && Value is not null;
}

public sealed record ThetaBetaResultPreview(
    string Channel,
    ThetaBetaResultPoint? StaticPoint,
    IReadOnlyList<ThetaBetaResultPoint> DynamicPoints)
{
    public bool IsDynamic => DynamicPoints.Count > 0;

    public ThetaBetaResultPoint? CurrentAt(double cursorSeconds) => IsDynamic
        ? DynamicPoints.LastOrDefault(point => point.EndSeconds <= cursorSeconds + 1e-9)
        : StaticPoint;

    public IReadOnlyList<ThetaBetaResultPoint> VisibleThrough(double cursorSeconds) => IsDynamic
        ? DynamicPoints.Where(point => point.EndSeconds <= cursorSeconds + 1e-9).ToArray()
        : [];

    public static ThetaBetaResultPreview? Parse(JsonElement? summary)
    {
        if (summary is not { ValueKind: JsonValueKind.Object } root ||
            !root.TryGetProperty("metric", out var metric) || metric.ValueKind != JsonValueKind.Object ||
            !metric.TryGetProperty("official", out var official) || Text(official, "algorithm_id") != "theta_beta" ||
            !metric.TryGetProperty("output", out var output) || Text(output, "unit") != "dimensionless")
            return null;

        var channel = Text(metric, "channel");
        if (metric.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
        {
            var points = series.EnumerateArray().Select(ParsePoint).Where(point => point is not null)
                .Cast<ThetaBetaResultPoint>().OrderBy(point => point.EndSeconds).ToArray();
            return new ThetaBetaResultPreview(channel, null, points);
        }

        var range = metric.TryGetProperty("actual_range", out var actual) ? actual : default;
        var value = Number(output, "value") ?? Number(metric, "value");
        return new ThetaBetaResultPreview(channel, new ThetaBetaResultPoint(
            double.NaN, Number(range, "start_s") ?? double.NaN, Number(range, "end_s") ?? double.NaN,
            value, value is null ? "Unavailable" : "Complete", Quality(metric), Failure(metric), Evidence(official)), []);
    }

    private static ThetaBetaResultPoint? ParsePoint(JsonElement point)
    {
        var time = Number(point, "time_s");
        var start = Number(point, "window_start_s");
        var end = Number(point, "window_end_s");
        if (time is null || start is null || end is null || end <= start) return null;
        var state = Text(point, "analysis_state");
        return new ThetaBetaResultPoint(time.Value, start.Value, end.Value,
            state == "Complete" ? Number(point, "value") : null,
            state, Quality(point), Failure(point), Evidence(point));
    }

    private static ThetaBetaEvidence Evidence(JsonElement point)
    {
        var official = point.ValueKind == JsonValueKind.Object && point.TryGetProperty("theta_beta_evidence", out _)
            ? point
            : point.TryGetProperty("official", out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
        var evidence = official.ValueKind == JsonValueKind.Object && official.TryGetProperty("theta_beta_evidence", out var direct) && direct.ValueKind == JsonValueKind.Object
            ? direct : default;
        var trace = point.TryGetProperty("calculation_trace", out var traceValue) && traceValue.ValueKind == JsonValueKind.Object ? traceValue : default;
        return new ThetaBetaEvidence(
            Number(evidence, "iapf_hz"), Range(evidence, "theta_range_hz", 0), Range(evidence, "theta_range_hz", 1),
            Range(evidence, "beta_range_hz", 0), Range(evidence, "beta_range_hz", 1),
            Number(evidence, "theta_power_uv2"), Number(evidence, "beta_power_uv2"),
            Text(trace, "formula"));
    }

    private static double? Range(JsonElement value, string key, int index) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var range) &&
        range.ValueKind == JsonValueKind.Array && range.GetArrayLength() > index
            ? Number(range[index]) : null;

    private static string Quality(JsonElement value) => value.TryGetProperty("quality", out var quality) ? Text(quality, "status") : "";

    private static string Failure(JsonElement value)
    {
        if (value.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object)
            return Text(failure, "message") is { Length: > 0 } message ? message : Text(failure, "code");
        if (value.TryGetProperty("quality", out var quality) && quality.ValueKind == JsonValueKind.Object &&
            quality.TryGetProperty("reasons", out var reasons) && reasons.ValueKind == JsonValueKind.Array)
            return string.Join("、", reasons.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()));
        return "";
    }

    private static string Text(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString() ?? "" : "";

    private static double? Number(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) ? Number(item) : null;

    private static double? Number(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
}
