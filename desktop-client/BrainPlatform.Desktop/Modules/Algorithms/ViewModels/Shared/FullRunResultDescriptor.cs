using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

/// <summary>
/// Describes the transport shape of a stored scientific result. Numerical
/// computation remains backend-owned; this descriptor only defines validation
/// and projection semantics for a completed window.
/// </summary>
internal sealed record FullRunResultDescriptor(
    string Id,
    string Kind,
    IReadOnlyList<string> OutputArrays,
    IReadOnlyList<string> RequiredAxes,
    string TimeAxis,
    string TimeSemantics,
    IReadOnlyList<string> RequiredProvenance,
    int CompatibilityVersion)
{
    public static FullRunResultDescriptor For(JsonElement? output)
    {
        var id = Text(output, "id");
        var kind = Text(output, "kind");
        return (id, kind) switch
        {
            ("psd", _) or (null, "frequency_series") => new(
                "psd", "frequency_series", ["psd"], ["frequency_hz"],
                "frequency_hz", "absolute_recording_frequency_hz", [], 1),
            ("stft", _) or (null, "time_frequency") => new(
                "stft", "time_frequency", ["power_linear", "power_db"],
                ["time_center_s", "frequency_hz"], "time_center_s",
                "relative_window_time_center_s",
                ["preprocessing_scope", "inner_time_axis", "analysis_state"], 1),
            _ => throw new InvalidOperationException(
                $"完整结果输出类型 {id ?? kind ?? "unknown"} 没有已注册的结果契约。"),
        };
    }

    public void Validate(StructuredPreviewResponse preview)
    {
        foreach (var axis in RequiredAxes)
            RequireAxis(preview.Axes, axis);
        foreach (var name in OutputArrays)
            if (preview.Arrays.ContainsKey(name))
                continue;
            else
                throw new InvalidOperationException($"完整结果缺少算法输出数组 {name}。");

        if (Kind == "time_frequency" && preview.AxisMetadata is not null &&
            preview.AxisMetadata.TryGetValue(TimeAxis, out var metadata) &&
            Text(metadata, "unit") is not "s")
            throw new InvalidOperationException("STFT 时间轴必须显式声明秒单位。");
    }

    private static void RequireAxis(IReadOnlyDictionary<string, JsonElement> axes, string name)
    {
        if (!axes.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.Array ||
            value.GetArrayLength() == 0)
            throw new InvalidOperationException($"完整结果缺少 {name} 坐标轴。");
    }

    private static string? Text(JsonElement? value, string name) =>
        value is { ValueKind: JsonValueKind.Object } objectValue &&
        objectValue.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;
}

internal static class FullRunResultDescriptors
{
    public static FullRunResultDescriptor For(JsonElement? output) =>
        FullRunResultDescriptor.For(output);
}
