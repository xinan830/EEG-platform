using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed record TnkChildDefinition(string AlgorithmId, string DisplayName, string RequiredRolesText);

public sealed record TnkChildResultDisplay(
    string AlgorithmId,
    string DisplayName,
    string RequiredRolesText,
    string ChannelText,
    string ValueText,
    string QualityText,
    string FailureText)
{
    public TnkResultChart Chart { get; init; } = new(false, []);
    public string? FailureCode { get; init; }
}

internal static class TnkGroupResultProjection
{
    internal static IReadOnlyList<TnkChildDefinition> ReadDefinitions(JsonElement outputSchema)
    {
        if (outputSchema.ValueKind != JsonValueKind.Object ||
            !outputSchema.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
            return [];

        return children.EnumerateArray()
            .Where(child => child.ValueKind == JsonValueKind.Object)
            .Select(child => new TnkChildDefinition(
                Text(child, "algorithm_id", "未知模块"),
                Text(child, "display_name_zh", "未命名指标"),
                Roles(child)))
            .ToArray();
    }

    internal static IReadOnlyList<TnkChildResultDisplay> ReadResults(AnalysisRunResponse? run, JsonElement outputSchema)
    {
        var definitions = ReadDefinitions(outputSchema);
        if (run?.ResultSummary is not { ValueKind: JsonValueKind.Object } summary ||
            !summary.TryGetProperty("structured", out var structured) || structured.ValueKind != JsonValueKind.Object ||
            !structured.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Object ||
            Text(output, "id", "") != "tnk_biofeedback" ||
            !structured.TryGetProperty("official", out var official) || official.ValueKind != JsonValueKind.Object ||
            !official.TryGetProperty("tnk_evidence", out var evidence) || evidence.ValueKind != JsonValueKind.Object)
            return [];

        if (!evidence.TryGetProperty("children", out var childResults) || childResults.ValueKind != JsonValueKind.Object)
        {
            if (!evidence.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array || points.GetArrayLength() == 0)
                return [];
            var latest = points[points.GetArrayLength() - 1];
            if (!latest.TryGetProperty("extensions", out var extensions) || extensions.ValueKind != JsonValueKind.Object ||
                !extensions.TryGetProperty("tnk_evidence", out var latestEvidence) || latestEvidence.ValueKind != JsonValueKind.Object ||
                !latestEvidence.TryGetProperty("children", out childResults) || childResults.ValueKind != JsonValueKind.Object)
                return [];
        }

        return definitions.Select(definition => ProjectChild(definition, childResults)).ToArray();
    }

    internal static TnkChildResultDisplay ProjectChild(TnkChildDefinition definition, JsonElement allResults)
    {
        var rows = allResults.TryGetProperty(definition.AlgorithmId, out var childRows) && childRows.ValueKind == JsonValueKind.Array
            ? childRows.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToArray()
            : [];
        if (rows.Length == 0)
            return new(definition.AlgorithmId, definition.DisplayName, definition.RequiredRolesText,
                "未返回", "不可用", "未返回", "后端未返回该子指标结果。");

        return new(definition.AlgorithmId, definition.DisplayName, definition.RequiredRolesText,
            string.Join("\n", rows.Select(row => Text(row, "channel", "未提供"))),
            string.Join("\n", rows.Select(Values)),
            string.Join("、", rows.Select(row => AlgorithmResultFormatter.FormatQualityStatus(Text(row, "quality", "未提供"))).Distinct()),
            string.Join("\n", rows.Select(Failure).Where(reason => !string.IsNullOrWhiteSpace(reason)).Distinct()))
        {
            FailureCode = rows.Select(FailureCodeOf).FirstOrDefault(code => !string.IsNullOrWhiteSpace(code))
        };
    }

    private static string? FailureCodeOf(JsonElement row) =>
        row.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object
            ? Text(failure, "code", "") : null;

    private static string Roles(JsonElement child)
    {
        if (!child.TryGetProperty("required_roles", out var roles) || roles.ValueKind != JsonValueKind.Array || roles.GetArrayLength() == 0)
            return "使用组内已计算指标";
        return string.Join("、", roles.EnumerateArray().Select(role => role.GetString()).Where(role => !string.IsNullOrWhiteSpace(role)));
    }

    private static string Values(JsonElement row)
    {
        if (row.TryGetProperty("output_values", out var outputs) && outputs.ValueKind == JsonValueKind.Object)
        {
            var outputUnit = Text(row, "unit", "");
            var suffix = string.IsNullOrWhiteSpace(outputUnit) ? "" : $" {outputUnit}";
            return $"{Text(row, "channel", "来源未提供")}：" +
                string.Join("；", outputs.EnumerateObject().Select(item => $"{item.Name}={DisplayValue(item.Value)}{suffix}"));
        }
        var value = row.TryGetProperty("value", out var scalar) ? DisplayValue(scalar) : "不可用";
        var unit = Text(row, "unit", "");
        return string.IsNullOrWhiteSpace(unit) ? value : $"{value} {unit}";
    }

    private static string Failure(JsonElement row)
    {
        if (row.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object)
        {
            var code = Text(failure, "code", "运行失败");
            var message = Text(failure, "message", "");
            return string.IsNullOrWhiteSpace(message) ? code : $"{code}：{message}";
        }

        return Text(row, "quality", "") switch
        {
            "partial" => "部分完成：部分窗口或依赖指标不可用。",
            "degraded" => "降级结果：部分依赖指标不可用。",
            "insufficient_data" => "数据不足：尚未完成预热或当前窗口长度不足。",
            "unavailable" => "不可用：当前窗口不满足该指标的计算条件。",
            "gate_failed" => "质量门失败：当前窗口未通过质量检查。",
            _ => "",
        };
    }

    private static string Text(JsonElement value, string property, string fallback) =>
        value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? fallback
            : fallback;

    private static string DisplayValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => "不可用",
        JsonValueKind.Number when value.TryGetDouble(out var number) && double.IsFinite(number) =>
            number.ToString("G6", System.Globalization.CultureInfo.InvariantCulture),
        JsonValueKind.String => value.GetString() ?? "不可用",
        JsonValueKind.True => "是",
        JsonValueKind.False => "否",
        _ => value.ToString(),
    };
}
