using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels;

internal static class AlgorithmResultFormatter
{
    internal static string BuildDynamicSeriesSummary(AnalysisRunResponse run)
    {
        if (run.ResultSummary is not { } summary || !summary.TryGetProperty("metric", out var metric) || metric.ValueKind != JsonValueKind.Object)
            return "动态结果未返回可展示的时间序列。";
        if (!metric.TryGetProperty("series", out var series) || series.ValueKind != JsonValueKind.Array)
            return "动态结果没有时间序列；请查看结构化预览或运行状态。";
        var points = series.EnumerateArray().ToArray();
        var states = points.GroupBy(point => point.TryGetProperty("analysis_state", out var state) ? state.GetString() ?? "未知" : "未知")
            .Select(group => $"{group.Key}={group.Count()}");
        var output = metric.TryGetProperty("output", out var outputElement) && outputElement.ValueKind == JsonValueKind.Object
            ? outputElement : default;
        var unit = output.ValueKind == JsonValueKind.Object && output.TryGetProperty("unit", out var unitElement)
            ? unitElement.GetString() ?? "" : "";
        return $"动态时间序列：{points.Length} 个窗口；状态：{string.Join("、", states)}；单位：{unit}。数值与状态由后端返回。";
    }

    internal static string BuildResultSummary(AnalysisRunResponse run)
    {
        if (run.ResultSummary is not { } summary || summary.ValueKind != JsonValueKind.Object)
            return "后端未返回结果摘要；结果保持不可用。";

        if (!summary.TryGetProperty("metric", out var metric) || metric.ValueKind != JsonValueKind.Object)
            return "后端已保存频谱结果；曲线与单位见结构化预览。";

        if (metric.TryGetProperty("series", out var dynamicSeries) && dynamicSeries.ValueKind == JsonValueKind.Array)
        {
            var points = dynamicSeries.EnumerateArray().ToArray();
            var latest = points.LastOrDefault(HasUsableDynamicValue);
            var windowText = latest.ValueKind == JsonValueKind.Object && latest.TryGetProperty("time_s", out var time) && time.TryGetDouble(out var seconds)
                ? $"最近有效窗口 {seconds:0.###} 秒"
                : "暂无有效窗口";
            var status = latest.ValueKind == JsonValueKind.Object && latest.TryGetProperty("quality", out var latestQuality)
                ? FormatQuality(latestQuality)
                : "未提供";
            if (latest.ValueKind == JsonValueKind.Object && latest.TryGetProperty("band_values", out var latestBands) && latestBands.ValueKind == JsonValueKind.Object)
            {
                var bandText = string.Join("；", latestBands.EnumerateObject().Select(item =>
                    $"{FormatBandName(item.Name)}：{FormatJsonValue(item.Value)}"));
                return $"动态结果：{points.Length} 个窗口；{windowText}；{bandText}；单位：ratio；状态：{status}";
            }
            var latestValue = latest.ValueKind == JsonValueKind.Object && latest.TryGetProperty("value", out var latestValueElement)
                ? FormatJsonValue(latestValueElement)
                : "不可用";
            var latestUnit = metric.TryGetProperty("output", out var dynamicOutput) && dynamicOutput.ValueKind == JsonValueKind.Object &&
                             dynamicOutput.TryGetProperty("unit", out var dynamicUnit)
                ? dynamicUnit.GetString() ?? ""
                : "";
            return $"动态结果：{points.Length} 个窗口；{windowText}；指标：{latestValue}{(string.IsNullOrWhiteSpace(latestUnit) ? "" : $" {latestUnit}")}；状态：{status}";
        }

        var output = metric.TryGetProperty("output", out var outputElement) && outputElement.ValueKind == JsonValueKind.Object
            ? outputElement
            : default;
        var value = output.ValueKind == JsonValueKind.Object && output.TryGetProperty("value", out var valueElement)
            ? FormatJsonValue(valueElement)
            : "不可用";
        var unit = output.ValueKind == JsonValueKind.Object && output.TryGetProperty("unit", out var unitElement)
            ? unitElement.GetString() ?? ""
            : "";
        var quality = metric.TryGetProperty("quality", out var qualityElement)
            ? FormatQuality(qualityElement)
            : "未提供";
        var channel = metric.TryGetProperty("channel", out var channelElement)
            ? channelElement.GetString() ?? ""
            : "";
        if (metric.TryGetProperty("band_values", out var bands) && bands.ValueKind == JsonValueKind.Object)
        {
            var bandText = string.Join("；", bands.EnumerateObject().Select(item =>
                $"{FormatBandName(item.Name)}：{FormatJsonValue(item.Value)}"));
            return $"频段相对功率：{bandText}；单位：ratio；通道：{(string.IsNullOrWhiteSpace(channel) ? "未指定" : channel)}；质量：{quality}";
        }
        return $"指标：{value}{(string.IsNullOrWhiteSpace(unit) ? "" : $" {unit}")}；通道：{(string.IsNullOrWhiteSpace(channel) ? "未指定" : channel)}；质量：{quality}";
    }

    internal static string BuildProvenance(AnalysisRunResponse run)
    {
        var provenance = run.AnalysisProvenance is { } value && value.ValueKind == JsonValueKind.Object
            ? value
            : default;
        var version = run.ScientificVersion ??
            (provenance.ValueKind == JsonValueKind.Object && provenance.TryGetProperty("scientific_algorithm_version", out var versionElement)
                ? versionElement.GetString() ?? "未知"
                : "未知");
        var mode = provenance.ValueKind == JsonValueKind.Object && provenance.TryGetProperty("mode", out var modeElement)
            ? modeElement.GetString() ?? "未知"
            : "未知";
        var channel = provenance.ValueKind == JsonValueKind.Object && provenance.TryGetProperty("channel", out var channelElement)
            ? channelElement.GetString() ?? "未指定"
            : "未指定";
        return $"Run：{run.RunId}；科学版本：{version}；模式：{mode}；通道：{channel}；请求范围：{FormatRange(run.RequestedRange)}；实际范围：{FormatRange(run.ActualRange)}";
    }

    internal static string BuildStructuredPreview(StructuredPreviewResponse preview)
    {
        var outputKind = preview.Output is { } output && output.ValueKind == JsonValueKind.Object && output.TryGetProperty("kind", out var kind)
            ? kind.GetString() ?? "结构化结果"
            : "结构化结果";
        var axes = preview.Axes.Count == 0
            ? "无轴信息"
            : string.Join("；", preview.Axes.Select(item =>
            {
                var unit = MetadataUnit(preview.AxisMetadata, item.Key);
                return $"{item.Key}[{unit}]={PreviewValues(item.Value)}";
            }));
        var arrays = preview.Arrays.Count == 0
            ? "无数值矩阵"
            : string.Join("；", preview.Arrays.Select(item =>
            {
                var unit = MetadataUnit(preview.ArrayMetadata, item.Key);
                return $"{item.Key}[{unit}]={PreviewValues(item.Value)}";
            }));
        var states = preview.WindowStateCounts.Count == 0
            ? ""
            : $"；窗口状态：{string.Join("、", preview.WindowStateCounts.Select(item => $"{item.Key}={item.Value}"))}";
        return $"类型：{outputKind}；通道：{string.Join("、", preview.ChannelOrder)}；轴：{axes}；数组：{arrays}{states}。数值与单位由后端返回，未在客户端重算。";
    }

    internal static string FormatBandValue(JsonElement values, string key) =>
        values.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)
            : "不可用";

    internal static string MetadataUnit(IReadOnlyDictionary<string, JsonElement> metadata, string key)
    {
        if (metadata.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Object && value.TryGetProperty("unit", out var unit))
            return unit.GetString() ?? "未声明单位";
        return "未声明单位";
    }

    private static string FormatBandName(string name) => name.ToLowerInvariant() switch
    {
        "delta" => "Delta",
        "theta" => "Theta",
        "alpha" => "Alpha",
        "beta" => "Beta",
        _ => name,
    };

    private static bool HasUsableDynamicValue(JsonElement point)
    {
        if (point.ValueKind != JsonValueKind.Object) return false;
        if (point.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var scalar) && double.IsFinite(scalar)) return true;
        return point.TryGetProperty("band_values", out var bands) && bands.ValueKind == JsonValueKind.Object &&
               bands.EnumerateObject().Any(item => item.Value.ValueKind == JsonValueKind.Number &&
                                                   item.Value.TryGetDouble(out var number) && double.IsFinite(number));
    }

    private static string FormatQuality(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? "未提供";
        if (value.ValueKind != JsonValueKind.Object)
            return FormatJsonValue(value);
        var status = value.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        var reasons = value.TryGetProperty("reasons", out var reasonsElement) && reasonsElement.ValueKind == JsonValueKind.Array
            ? string.Join("、", reasonsElement.EnumerateArray().Select(FormatJsonValue))
            : "";
        return string.IsNullOrWhiteSpace(reasons)
            ? status switch { "clean" => "良好（clean）", "gate_failed" => "未通过质量门", _ => status ?? "未提供" }
            : $"{status ?? "未提供"}：{reasons}";
    }

    private static string FormatRange(TimeRange? range) => range is null
        ? "未提供"
        : $"{range.StartSeconds:0.###}–{range.EndSeconds:0.###} s";

    private static string PreviewValues(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return FormatJsonValue(value);
        var values = value.EnumerateArray().Take(3).Select(PreviewValue).ToArray();
        return values.Length == 0 ? "[]" : $"[{string.Join(", ", values)}{(value.GetArrayLength() > values.Length ? ", …" : "")}]";
    }

    private static string PreviewValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? "[…]" : FormatJsonValue(value);

    private static string FormatJsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => "不可用",
        JsonValueKind.Number => value.ToString(),
        JsonValueKind.String => value.GetString() ?? "不可用",
        JsonValueKind.True => "是",
        JsonValueKind.False => "否",
        _ => value.ToString(),
    };
}
