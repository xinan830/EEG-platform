using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

/// <summary>Checks the supported result boundary; never converts another shape.</summary>
internal static class CurrentAlgorithmResult
{
    internal const string Version = "official-algorithm-result-v1";
    internal const string FrameVersion = "dynamic-analysis-frame-v6";

    internal static void Require(AnalysisRunResponse run)
    {
        if (run.AnalysisType == "official_algorithm" && run.Status == "completed")
        {
            if (run.ResultSummary is not { } summary)
                throw Invalid();
            Require(summary);
        }
    }

    internal static void Require(JsonElement summary)
    {
        if (Text(summary, "result_contract_version") != Version) throw Invalid();
        var hasMetric = Object(summary, "metric", out var metric);
        var hasStructured = Object(summary, "structured", out var structured);
        if (hasMetric == hasStructured) throw Invalid();
        if (hasStructured)
        {
            if (!Object(structured, "output", out var output) || Text(output, "kind").Length == 0)
                throw Invalid();
            RejectDuplicates(structured);
            if (!Object(structured, "actual_range", out var actual) || !Range(actual, "start_s", "end_s") ||
                !Object(structured, "requested_range", out var requested) || !Range(requested, "start_s", "end_s") ||
                !structured.TryGetProperty("channel_order", out var channels) || channels.ValueKind != JsonValueKind.Array || channels.GetArrayLength() == 0 ||
                !Object(structured, "axes", out var axes) || !axes.EnumerateObject().Any() ||
                !Object(structured, "arrays", out var arrays) || !arrays.EnumerateObject().Any())
                throw Invalid();
            foreach (var axis in axes.EnumerateObject())
                if (Text(axis.Value, "unit").Length == 0 || Text(axis.Value, "array_key").Length == 0 ||
                    !Integer(axis.Value, "length", out var length) || length < 0 ||
                    !Object(arrays, Text(axis.Value, "array_key"), out var axisArray) ||
                    !axisArray.TryGetProperty("shape", out var axisShape) || axisShape.ValueKind != JsonValueKind.Array ||
                    axisShape.GetArrayLength() != 1 || axisShape[0].ValueKind != JsonValueKind.Number ||
                    !axisShape[0].TryGetInt64(out var size) || size != length) throw Invalid();
            foreach (var array in arrays.EnumerateObject())
                if (Text(array.Value, "unit").Length == 0 || !array.Value.TryGetProperty("shape", out var shape) ||
                    shape.ValueKind != JsonValueKind.Array || shape.EnumerateArray().Any(value =>
                        value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var size) || size < 0))
                    throw Invalid();
            if (Text(output, "mode") == "dynamic")
            {
                if (!structured.TryGetProperty("windows", out var windows) || windows.ValueKind != JsonValueKind.Array)
                    throw Invalid();
                foreach (var window in windows.EnumerateArray())
                {
                    RejectDuplicates(window);
                    if (!State(Text(window, "state")) || !Range(window, "start_s", "end_s") ||
                        !Integer(window, "start_sample", out var start) || !Integer(window, "end_sample", out var end) || start < 0 || end <= start ||
                        Text(window, "quality").Length == 0)
                        throw Invalid();
                }
            }
            else if (output.TryGetProperty("mode", out _) || structured.TryGetProperty("windows", out _)) throw Invalid();
            return;
        }
        RejectDuplicates(metric);
        RequireOutput(metric);
        if (!Object(metric, "actual_range", out var actualRange) || !Range(actualRange, "start_s", "end_s"))
            throw Invalid();
        if (Text(metric, "mode") != "dynamic")
        {
            if (metric.TryGetProperty("series", out _) || metric.TryGetProperty("mode", out _)) throw Invalid();
            return;
        }
        if (Text(metric, "result_contract_version") != FrameVersion ||
            !metric.TryGetProperty("series", out var series) || series.ValueKind != JsonValueKind.Array)
            throw Invalid();
        foreach (var point in series.EnumerateArray())
        {
            RejectDuplicates(point);
            RequireOutput(point);
            if (Text(point, "result_contract_version") != FrameVersion ||
                !State(Text(point, "analysis_state")) || !Range(point, "window_start_s", "window_end_s") ||
                !Number(point, "time_s", out var time) || time != point.GetProperty("window_end_s").GetDouble() ||
                !Object(point, "quality", out _))
                throw Invalid();
        }
    }

    private static void RequireOutput(JsonElement item)
    {
        if (!Object(item, "output", out var output) || Text(output, "unit").Length == 0 ||
            !output.TryGetProperty("value", out var value) ||
            value.ValueKind != JsonValueKind.Null &&
            (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number)))
            throw Invalid();
    }

    private static void RejectDuplicates(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || item.TryGetProperty("value", out _) || item.TryGetProperty("warmup", out _))
            throw Invalid();
    }

    private static bool State(string state) => state is "Partial" or "Complete" or "Rejected" or "Unavailable";
    private static bool Range(JsonElement item, string start, string end) =>
        Number(item, start, out var first) && Number(item, end, out var last) && first >= 0 && last > first;
    private static bool Number(JsonElement item, string key, out double number)
    {
        number = double.NaN;
        return item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number) && double.IsFinite(number);
    }
    private static bool Integer(JsonElement item, string key, out long number)
    {
        number = 0;
        return item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out number);
    }
    private static bool Object(JsonElement item, string key, out JsonElement value)
    {
        value = default;
        return item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out value) && value.ValueKind == JsonValueKind.Object;
    }
    private static string Text(JsonElement item, string key) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
    private static InvalidOperationException Invalid() => new("结果不符合当前算法结果契约，请使用当前版本重新运行。");
}
