using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

/// <summary>
/// Projects a completed scalar/multi-metric dynamic Run into the common
/// single-window contract. It only reads backend values; it never calculates.
/// </summary>
internal static class FullRunMetricWindowPreviewAdapter
{
    public static bool TryCreate(
        AnalysisRunResponse run,
        DynamicWindowRequest target,
        double? samplingRateHz,
        string valueField,
        IReadOnlyList<string>? multiMetricFields,
        out DynamicWindowResponse response)
    {
        response = null!;
        CurrentAlgorithmResult.Require(run);
        if (run.Status != "completed" ||
            samplingRateHz is not > 0 || !double.IsFinite(samplingRateHz.Value) ||
            run.ResultSummary is not { ValueKind: JsonValueKind.Object } summary ||
            !summary.TryGetProperty("metric", out var metric) ||
            metric.ValueKind != JsonValueKind.Object ||
            !metric.TryGetProperty("series", out var series) ||
            series.ValueKind != JsonValueKind.Array)
            return false;

        var cursorSeconds = target.CursorSample / samplingRateHz.Value;
        var selected = Select(series, cursorSeconds);
        if (selected is null)
        {
            response = new DynamicWindowResponse(
                $"full-metric:{run.RunId}", $"full-metric:{run.RunId}:{target.CursorSample}",
                target.RequestId ?? string.Empty, "insufficient_data", Identity(target.RecordingId),
                null, null, "unavailable",
                new DynamicWindowFailure("NO_COMPLETED_WINDOW", "完整结果没有覆盖当前游标的窗口"), null);
            return true;
        }

        var index = selected.Value.Index;
        var point = selected.Value.Point;
        var startSeconds = Number(point, "window_start_s");
        var endSeconds = Number(point, "window_end_s");
        if (startSeconds is null || endSeconds is null || endSeconds <= startSeconds)
            return false;

        var stateText = Text(point, "analysis_state");
        var quality = Quality(point);
        var state = stateText switch
        {
            "Complete" => "ready",
            "Partial" => "ready",
            "Rejected" => "rejected",
            "Unavailable" => "unavailable",
            _ => "unavailable",
        };
        var failure = Failure(point);
        if (state is "rejected" or "unavailable")
        {
            response = new DynamicWindowResponse(
                $"full-metric:{run.RunId}", $"full-metric:{run.RunId}:window-{index}",
                target.RequestId ?? string.Empty, state, Identity(target.RecordingId), null,
                Range(startSeconds.Value, endSeconds.Value, samplingRateHz.Value), quality, failure, null);
            return true;
        }

        var output = point.TryGetProperty("output", out var outputElement) &&
            outputElement.ValueKind == JsonValueKind.Object ? outputElement : default;
        var unit = Text(output, "unit");
        var outputKind = multiMetricFields is { Count: > 0 } ? "multi_metric_series" : "scalar_series";
        var arrays = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var units = new Dictionary<string, string>(StringComparer.Ordinal);
        if (multiMetricFields is { Count: > 0 })
        {
            if (!point.TryGetProperty("band_values", out var bandValues) || bandValues.ValueKind != JsonValueKind.Object)
                return false;
            foreach (var field in multiMetricFields)
            {
                if (!bandValues.TryGetProperty(field, out var value) || !Finite(value))
                {
                    response = Unavailable(target, run, index, startSeconds.Value, endSeconds.Value,
                        samplingRateHz.Value, quality, "MISSING_METRIC_VALUE",
                        $"完整结果窗口缺少 {field} 的有效值");
                    return true;
                }
                arrays[field] = JsonSerializer.SerializeToElement(new[] { value.Clone() });
                units[field] = unit;
            }
        }
        else
        {
            if (!output.TryGetProperty("value", out var value) || !Finite(value)) return false;
            arrays[valueField] = JsonSerializer.SerializeToElement(new[] { value.Clone() });
            units[valueField] = unit;
        }

        var evidence = BuildEvidence(point, valueField);
        var provenance = JsonSerializer.SerializeToElement(new { analysis_state = stateText is "Partial" ? "Partial" : "Complete" });
        var result = new DynamicWindowResultResponse(
            Identity(target.RecordingId), state, outputKind,
            new Dictionary<string, JsonElement> { ["time_s"] = JsonSerializer.SerializeToElement(new[] { endSeconds.Value }) },
            new Dictionary<string, string> { ["time_s"] = "s" }, arrays, units, quality, null,
            evidence, provenance);
        response = new DynamicWindowResponse(
            $"full-metric:{run.RunId}", $"full-metric:{run.RunId}:window-{index}",
            target.RequestId ?? string.Empty, state, Identity(target.RecordingId), null,
            Range(startSeconds.Value, endSeconds.Value, samplingRateHz.Value), quality, null, result);
        return true;
    }

    private static (int Index, JsonElement Point)? Select(JsonElement series, double cursorSeconds)
    {
        (int Index, JsonElement Point)? selected = null;
        var index = 0;
        foreach (var point in series.EnumerateArray())
        {
            var end = Number(point, "window_end_s");
            if (point.ValueKind == JsonValueKind.Object && end is not null && end <= cursorSeconds)
                selected = (index, point);
            index++;
        }
        return selected;
    }

    private static DynamicWindowRange Range(double startSeconds, double endSeconds, double samplingRateHz) =>
        new((long)Math.Round(startSeconds * samplingRateHz), (long)Math.Round(endSeconds * samplingRateHz), startSeconds, endSeconds);

    private static DynamicWindowResponse Unavailable(
        DynamicWindowRequest target,
        AnalysisRunResponse run,
        int index,
        double startSeconds,
        double endSeconds,
        double samplingRateHz,
        string quality,
        string code,
        string message) =>
        new(
            $"full-metric:{run.RunId}", $"full-metric:{run.RunId}:window-{index}",
            target.RequestId ?? string.Empty, "unavailable", Identity(target.RecordingId), null,
            Range(startSeconds, endSeconds, samplingRateHz), quality,
            new DynamicWindowFailure(code, message), null);

    private static JsonElement BuildEvidence(JsonElement point, string valueField)
    {
        var extension = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (point.TryGetProperty("official", out var official) && official.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in official.EnumerateObject())
            {
                if (property.Name.EndsWith("_evidence", StringComparison.Ordinal))
                    extension[property.Name] = property.Value.Clone();
            }
        }
        return JsonSerializer.SerializeToElement(new
        {
            extensions = extension,
            calculation_trace = point.TryGetProperty("calculation_trace", out var trace) ? trace : JsonSerializer.SerializeToElement(new { }),
            value_field = valueField,
        });
    }

    private static DynamicWindowFailure? Failure(JsonElement point)
    {
        if (!point.TryGetProperty("failure", out var failure) || failure.ValueKind != JsonValueKind.Object) return null;
        var code = Text(failure, "code");
        var message = Text(failure, "message");
        return new DynamicWindowFailure(code.Length == 0 ? "FULL_WINDOW_UNAVAILABLE" : code,
            message.Length == 0 ? "完整结果窗口不可用" : message,
            failure.TryGetProperty("detail", out var details) ? details.Clone() : null);
    }

    private static string Quality(JsonElement point) =>
        point.TryGetProperty("quality", out var quality) && quality.ValueKind == JsonValueKind.Object
            ? Text(quality, "status") : "unknown";

    private static JsonElement Identity(string recordingId) =>
        JsonSerializer.SerializeToElement(new { recording_id = recordingId });

    private static string Text(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString() ?? string.Empty : string.Empty;

    private static double? Number(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) ? Number(item) : null;

    private static double? Number(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;

    private static bool Finite(JsonElement value) => Number(value) is not null;

}
