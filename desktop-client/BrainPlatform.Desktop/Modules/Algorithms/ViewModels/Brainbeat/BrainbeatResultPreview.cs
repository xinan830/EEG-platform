using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Brainbeat;

public sealed record BrainbeatPoint(
    double TimeSeconds,
    double? Value,
    string State,
    string Quality,
    string Failure,
    double StartSeconds = double.NaN,
    double EndSeconds = double.NaN);

public sealed class BrainbeatResultPreview
{
    private BrainbeatResultPreview(double? value, string unit, IReadOnlyList<BrainbeatPoint> points, string channel)
    {
        Value = value;
        Unit = unit;
        Points = points;
        Channel = channel;
    }

    public double? Value { get; }
    public string Unit { get; }
    public IReadOnlyList<BrainbeatPoint> Points { get; }
    public string Channel { get; }
    public bool IsDynamic => Points.Count > 0;

    public static BrainbeatResultPreview? Parse(AnalysisRunResponse? run)
    {
        if (run?.ResultSummary is not { ValueKind: JsonValueKind.Object } summary ||
            !summary.TryGetProperty("metric", out var metric) || metric.ValueKind != JsonValueKind.Object)
            return null;
        var channel = Text(metric, "channel");
        var unit = metric.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Object
            ? Text(output, "unit") : "ratio";
        if (metric.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
        {
            var points = series.EnumerateArray().Select(point =>
            {
                var time = Number(point, "time_s");
                var start = Number(point, "window_start_s");
                var end = Number(point, "window_end_s");
                // Older result payloads only exposed the window end time.
                // Keep the timeline usable without inventing a scientific value.
                if (!double.IsFinite(end)) end = time;
                if (!double.IsFinite(start)) start = double.NaN;
                return new BrainbeatPoint(time, NumberOrNull(point, "value"), Text(point, "analysis_state"),
                    Quality(point), Failure(point), start, end);
            }).ToArray();
            return new BrainbeatResultPreview(null, unit, points, channel);
        }
        var value = metric.TryGetProperty("value", out var direct) ? NumberOrNull(direct) :
            metric.TryGetProperty("output", out var result) && result.ValueKind == JsonValueKind.Object && result.TryGetProperty("value", out var nested)
                ? NumberOrNull(nested) : null;
        return new BrainbeatResultPreview(value, unit, [], channel);
    }

    private static string Quality(JsonElement point) => point.TryGetProperty("quality", out var quality)
        ? quality.ValueKind == JsonValueKind.Object ? Text(quality, "status") : quality.GetString() ?? "未提供" : "未提供";
    private static string Failure(JsonElement point) => point.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object
        ? Text(failure, "message") : "";
    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : "";
    private static double Number(JsonElement value, string name) => value.TryGetProperty(name, out var element) && element.TryGetDouble(out var number) ? number : double.NaN;
    private static double? NumberOrNull(JsonElement value, string name) => value.TryGetProperty(name, out var element) ? NumberOrNull(element) : null;
    private static double? NumberOrNull(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
}
