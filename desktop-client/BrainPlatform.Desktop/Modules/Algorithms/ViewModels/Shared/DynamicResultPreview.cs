using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed record DynamicSeriesPoint(
    double TimeSeconds,
    string ValueText,
    string Unit,
    string State,
    string Quality);

public sealed record DynamicWindowRow(
    double StartSeconds,
    double EndSeconds,
    string State,
    string Quality,
    string Failure);

public static class DynamicResultPreview
{
    public static IReadOnlyList<DynamicSeriesPoint> ParseScalar(AnalysisRunResponse run)
    {
        if (run.ResultSummary is not { } summary ||
            !summary.TryGetProperty("metric", out var metric) ||
            !metric.TryGetProperty("series", out var series) ||
            series.ValueKind != JsonValueKind.Array)
            return [];

        var unit = metric.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Object &&
                   output.TryGetProperty("unit", out var unitElement)
            ? unitElement.GetString() ?? ""
            : "";
        return series.EnumerateArray().Select(point =>
        {
            var time = Number(point, "time_s");
            var value = point.TryGetProperty("band_values", out var bands) && bands.ValueKind == JsonValueKind.Object
                ? string.Join("；", bands.EnumerateObject().Select(item => $"{item.Name}={FormatValue(item.Value)}"))
                : point.TryGetProperty("value", out var valueElement) ? FormatValue(valueElement) : "不可用";
            var state = point.TryGetProperty("analysis_state", out var stateElement) ? stateElement.GetString() ?? "未知" : "未知";
            var quality = point.TryGetProperty("quality", out var qualityElement) ? FormatQuality(qualityElement) : "未提供";
            return new DynamicSeriesPoint(time, value, unit, state, quality);
        }).ToArray();
    }

    public static IReadOnlyList<DynamicWindowRow> ParseStructured(StructuredPreviewResponse preview)
    {
        return preview.Windows.Select(window =>
        {
            var start = Number(window, "start_s");
            var end = Number(window, "end_s");
            var state = AlgorithmResultFormatter.FormatWindowStateForDisplay(String(window, "state"));
            var quality = AlgorithmResultFormatter.FormatQualityReasonForDisplay(String(window, "quality"));
            var failure = window.TryGetProperty("failure", out var failureElement) && failureElement.ValueKind == JsonValueKind.Object
                ? AlgorithmResultFormatter.FormatFailureCodeForDisplay(String(failureElement, "code"))
                : "";
            return new DynamicWindowRow(start, end, state, quality, failure);
        }).ToArray();
    }

    private static double Number(JsonElement value, string name) =>
        value.TryGetProperty(name, out var element) && element.TryGetDouble(out var number) ? number : double.NaN;

    private static string String(JsonElement value, string name) =>
        value.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? "未提供"
            : "未提供";

    private static string FormatValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetDouble(out var number) && double.IsFinite(number) => number.ToString("G6", System.Globalization.CultureInfo.InvariantCulture),
        JsonValueKind.Null => "不可用",
        _ => "不可用",
    };

    private static string FormatQuality(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "未提供";
        if (value.ValueKind != JsonValueKind.Object) return "未提供";
        var status = String(value, "status");
        return status switch
        {
            "clean" => "良好",
            "gate_failed" => "未通过质量门",
            _ => status,
        };
    }
}
