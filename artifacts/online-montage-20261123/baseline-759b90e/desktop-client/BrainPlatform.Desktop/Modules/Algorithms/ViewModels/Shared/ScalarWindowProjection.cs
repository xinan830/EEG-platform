using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

internal sealed record ScalarWindowProjection(
    double TimeSeconds,
    double StartSeconds,
    double EndSeconds,
    double? Value,
    string State,
    string Quality,
    string Failure,
    JsonElement Evidence,
    JsonElement CalculationTrace);

internal static class ScalarWindowProjectionParser
{
    internal static ScalarWindowProjection Parse(DynamicWindowResponse response, string field, string unit, string evidenceName)
    {
        var range = response.Range ?? throw new InvalidOperationException("标量按需窗口缺少实际范围。");
        if (!double.IsFinite(range.StartSeconds) || !double.IsFinite(range.EndSeconds) ||
            range.StartSeconds < 0 || range.EndSeconds <= range.StartSeconds ||
            range.StartSample < 0 || range.EndSample <= range.StartSample)
            throw new InvalidOperationException("标量按需窗口范围无效。");

        var value = (double?)null;
        var evidence = default(JsonElement);
        var trace = default(JsonElement);
        if (response.State == "ready")
        {
            var result = response.Result ?? throw new InvalidOperationException("可用标量窗口缺少结果。");
            if (result.State != "ready" || result.OutputKind != "scalar_series" || result.Quality != response.Quality ||
                result.Axes is null || result.AxisUnits is null || result.Arrays is null || result.ArrayUnits is null ||
                result.Axes.Count != 1 || result.Arrays.Count != 1 || result.AxisUnits.Count != 1 ||
                result.ArrayUnits.Count != 1 || result.AxisUnits.GetValueOrDefault("time_s") != "s" ||
                !result.Axes.TryGetValue("time_s", out var times) || SingleFinite(times) != range.EndSeconds ||
                !result.Arrays.TryGetValue(field, out var values) ||
                result.ArrayUnits.GetValueOrDefault(field) != unit || SingleFinite(values) is not double finiteValue)
                throw new InvalidOperationException($"{field} 按需结果的字段、形状、范围或单位无效。");
            value = finiteValue;
            if (result.Evidence is not { ValueKind: JsonValueKind.Object } payload ||
                !payload.TryGetProperty("extensions", out var extensions) || extensions.ValueKind != JsonValueKind.Object ||
                !extensions.TryGetProperty(evidenceName, out evidence) || evidence.ValueKind != JsonValueKind.Object ||
                !payload.TryGetProperty("calculation_trace", out trace) || trace.ValueKind != JsonValueKind.Object ||
                result.Provenance is not { ValueKind: JsonValueKind.Object } declared ||
                Text(declared, "analysis_state") is not ("Partial" or "Complete"))
                throw new InvalidOperationException($"{field} 按需结果缺少模块证据或窗口状态。");
        }

        var state = response.State switch
        {
            "ready" => response.Result?.Provenance is { ValueKind: JsonValueKind.Object } provenance &&
                       Text(provenance, "analysis_state") == "Partial" ? "Partial" : "Complete",
            "rejected" => "Rejected",
            "unavailable" or "insufficient_data" => "Unavailable",
            "cancelled" => "Cancelled",
            "failed" => "Failed",
            _ => response.State,
        };
        var failure = response.Failure is { } error
            ? $"{error.Code}：{error.Message}" + (error.Details is { } details ? $"\n{details.GetRawText()}" : "")
            : "";
        return new ScalarWindowProjection(range.EndSeconds, range.StartSeconds, range.EndSeconds,
            value, state, response.Quality ?? "unknown", failure, evidence, trace);
    }

    internal static IReadOnlyList<T> Merge<T>(IReadOnlyList<T> previous, T next,
        Func<T, double> start, Func<T, double> end)
    {
        const int maximumWindows = 256;
        return previous.Where(point => start(point) != start(next) || end(point) != end(next))
            .Append(next).OrderBy(end).TakeLast(maximumWindows).ToArray();
    }

    internal static double? Number(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) ? Number(item) : null;

    internal static string Text(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) &&
        item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";

    internal static double? Range(JsonElement value, string name, int index) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var range) &&
        range.ValueKind == JsonValueKind.Array && range.GetArrayLength() > index ? Number(range[index]) : null;

    private static double? SingleFinite(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 1 ? Number(value[0]) : null;

    private static double? Number(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number : null;
}
