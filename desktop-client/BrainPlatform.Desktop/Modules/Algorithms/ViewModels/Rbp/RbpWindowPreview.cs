using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;

public sealed partial class RbpResultPreview
{
    private const int MaximumVisitedWindows = 256;

    public static RbpResultPreview FromWindow(DynamicWindowResponse response, RbpResultPreview? previous = null)
    {
        var shares = new double?[CurrentBands.Length];
        var range = response.Range;
        if (response.State == "ready")
        {
            var result = response.Result ?? throw new InvalidOperationException("RBP 可用窗口缺少结果。");
            if (range is null || result.OutputKind != "multi_metric_series" || result.Quality != "clean" ||
                result.Axes is null || result.AxisUnits is null || result.Arrays is null || result.ArrayUnits is null ||
                result.Axes.Count != 1 || result.AxisUnits.Count != 1 || result.AxisUnits.GetValueOrDefault("time_s") != "s" ||
                !result.Axes.TryGetValue("time_s", out var time) || SingleValue(time) != range.EndSeconds ||
                result.Arrays.Count != CurrentBands.Length || result.ArrayUnits.Count != CurrentBands.Length)
                throw new InvalidOperationException("RBP 按需结果的字段、轴、范围或单位无效。");
            for (var index = 0; index < CurrentBands.Length; index++)
            {
                var name = CurrentBands[index].Key;
                if (!result.Arrays.TryGetValue(name, out var values) ||
                    result.ArrayUnits.GetValueOrDefault(name) != "ratio" ||
                    SingleValue(values) is not double value || value < 0 || value > 1)
                    throw new InvalidOperationException($"RBP {name} 不是有效单窗 ratio。");
                shares[index] = value;
            }
        }
        var state = response.State == "ready" ? "Complete" : response.State switch
        {
            "rejected" => "Rejected", "unavailable" => "Unavailable", _ => response.State
        };
        if (response.Result?.Provenance is { ValueKind: JsonValueKind.Object } provenance &&
            provenance.TryGetProperty("analysis_state", out var declaredState) &&
            declaredState.GetString() is "Partial" or "Complete") state = declaredState.GetString()!;
        var failure = response.Failure is { } error
            ? $"{error.Code}：{error.Message}" + (error.Details is { } details ? $"\n{details.GetRawText()}" : "") : "";
        var windows = range is null ? previous?.Windows ?? [] :
            (previous?.Windows ?? []).Where(window => window.StartSeconds != range.StartSeconds || window.EndSeconds != range.EndSeconds)
            .Append(new RbpWindowPoint(range.EndSeconds, range.StartSeconds, range.EndSeconds,
                state, response.Quality ?? "unknown", failure, shares))
            .TakeLast(MaximumVisitedWindows).OrderBy(window => window.EndSeconds).ToArray();
        return new RbpResultPreview(CurrentBands, shares, windows, response.Quality ?? "unknown", failure);
    }

    private static double? SingleValue(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array && array.GetArrayLength() == 1 &&
        array[0].ValueKind == JsonValueKind.Number && array[0].TryGetDouble(out var value) && double.IsFinite(value)
            ? value : null;
}
