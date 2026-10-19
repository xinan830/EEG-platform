using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

/// <summary>Validates transport identity and geometry, without computing scientific values.</summary>
internal static class FullRunPreviewIntegrity
{
    public static void RequireRun(AnalysisRunResponse run, AnalysisRunRequest request)
    {
        Require(run.RecordingId == request.RecordingId && run.AnalysisType == request.AnalysisType,
            "完整结果的记录或分析类型与请求不匹配。");
        Require(request.Config.TryGetProperty("scientific_version", out var version) &&
            version.ValueKind == JsonValueKind.String && version.GetString() == run.ScientificVersion,
            "完整结果的科学版本与请求不匹配。");
    }

    public static void RequireStructured(StructuredPreviewResponse preview, AnalysisRunResponse run)
    {
        Require(preview.RunId == run.RunId && preview.Artifact.RunId == run.RunId &&
            preview.ScientificVersion == run.ScientificVersion && !string.IsNullOrWhiteSpace(run.ScientificVersion),
            "完整结果缓存的 Run、文件来源或科学版本不匹配。");
        var previousEnd = -1L;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < preview.Windows.Count; index++)
        {
            var window = preview.Windows[index];
            var start = Integer(window, "start_sample");
            var end = Integer(window, "end_sample");
            Require(start >= 0 && end > start && end > previousEnd,
                "完整结果窗口范围无效或顺序不一致。");
            // Stored series use row position when no explicit window ID exists.
            var id = Text(window, "window_id") ?? $"window-{index}";
            Require(id.Length > 0 && ids.Add(id), "完整结果窗口标识无效或重复。");
            if (window.TryGetProperty("window_index", out _))
                Require(Integer(window, "window_index") == index, "完整结果窗口行号不一致。");
            previousEnd = end;
        }

        Require(preview.Windows.Count > 0, "完整结果缺少可验证的动态窗口元数据。");
        Require(preview.Arrays.Count > 0, "完整结果缺少窗口数组。");
        foreach (var values in preview.Arrays.Values)
        {
            Require(values.ValueKind == JsonValueKind.Array && values.GetArrayLength() == preview.Windows.Count,
                "完整结果数组行数与窗口数不一致。");
            foreach (var row in values.EnumerateArray())
                RequireGeometry(preview.Output, preview.Axes, row);
        }
    }

    public static void RequireIndexed(WindowPreviewResponse preview, AnalysisRunResponse run,
        DynamicWindowRequest target)
    {
        Require(preview.RunId == run.RunId && preview.RecordingId == target.RecordingId &&
            preview.CursorSample == target.CursorSample && preview.ScientificVersion == run.ScientificVersion,
            "完整结果窗口的 Run、记录、游标或科学版本不匹配。");
        if (preview.Window is not { } window)
        {
            Require(preview.Status != "available", "完整结果声明可用但缺少窗口。");
            return;
        }
        Require(window.WindowIndex >= 0 && window.StartSample >= 0 &&
            window.EndSample > window.StartSample && window.EndSample <= target.CursorSample,
            "完整结果窗口范围无效或超前于游标。");
        if (preview.Status != "available") return;
        Require(window.QualityStatus == "valid" && window.WindowState is "Complete" or "Partial",
            "完整结果窗口可用状态与质量不一致。");
        Require(preview.Arrays is { Count: > 0 } && preview.Axes is { Count: > 0 },
            "完整结果窗口缺少数组或坐标轴。");
        var axes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in preview.Axes!) axes[item.Key] = item.Value;
        if (preview.AxisMetadata is { } metadata)
            foreach (var item in metadata)
                if (Text(item.Value, "array_key") is { } key && preview.Axes.TryGetValue(key, out var values))
                    axes[item.Key] = values;
        foreach (var row in preview.Arrays!.Values) RequireGeometry(preview.Output, axes, row);
    }

    private static void RequireGeometry(JsonElement? output,
        IReadOnlyDictionary<string, JsonElement> axes, JsonElement row)
    {
        var kind = output is { } value ? Text(value, "kind") : null;
        if (kind == "frequency_series")
            RequireVector(row, AxisLength(axes, "frequency_hz"));
        else if (kind == "time_frequency")
        {
            Require(row.ValueKind == JsonValueKind.Array && row.GetArrayLength() == AxisLength(axes, "time_center_s"),
                "完整结果时频矩阵的时间维度不一致。");
            var frequencies = AxisLength(axes, "frequency_hz");
            foreach (var vector in row.EnumerateArray()) RequireVector(vector, frequencies);
        }
        else
            throw new InvalidOperationException("完整结果的数组输出结构不可验证。");
    }

    private static int AxisLength(IReadOnlyDictionary<string, JsonElement> axes, string name)
    {
        Require(axes.TryGetValue(name, out var axis) && axis.ValueKind == JsonValueKind.Array &&
            axis.GetArrayLength() > 0, $"完整结果缺少 {name} 坐标轴。");
        return axis.GetArrayLength();
    }

    private static void RequireVector(JsonElement row, int length)
    {
        Require(row.ValueKind == JsonValueKind.Array && row.GetArrayLength() == length,
            "完整结果数组与坐标轴维度不一致。");
        foreach (var cell in row.EnumerateArray())
            Require(cell.ValueKind is JsonValueKind.Number or JsonValueKind.Null,
                "完整结果数组包含非数值结构。");
    }

    private static long Integer(JsonElement value, string key)
    {
        Require(value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) &&
            item.ValueKind == JsonValueKind.Number, $"完整结果窗口缺少 {key}。");
        Require(value.GetProperty(key).TryGetInt64(out var number), $"完整结果窗口 {key} 不是整数。");
        return number;
    }

    private static string? Text(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) &&
        item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
