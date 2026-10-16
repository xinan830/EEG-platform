using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

/// <summary>
/// Converts backend-owned Full Run window data into the common single-window
/// preview contract. No EEG calculation is performed here.
/// </summary>
internal static class FullRunWindowPreviewAdapter
{
    public static DynamicWindowResponse ToDynamicWindowResponse(
        StructuredPreviewResponse preview,
        DynamicWindowRequest target,
        double? samplingRateHz)
    {
        if (samplingRateHz is not > 0 || !double.IsFinite(samplingRateHz.Value))
            throw new InvalidOperationException("完整结果窗口缺少有效采样率，无法建立时间范围。");
        var selected = SelectWindow(preview.Windows, target.CursorSample);
        if (selected is null)
        {
            return new DynamicWindowResponse(
                $"full-cache:{preview.RunId}", $"full-cache:{preview.RunId}:{target.CursorSample}",
                target.RequestId ?? string.Empty, "insufficient_data", Identity(target.RecordingId),
                null, null, "unavailable",
                new DynamicWindowFailure("NO_COMPLETED_WINDOW", "完整结果没有覆盖当前游标的窗口"), null);
        }

        var rate = samplingRateHz.Value;
        var index = selected.Value.Index;
        var window = selected.Value.Window;
        var state = WindowState(window);
        var quality = WindowQuality(window);
        if (quality is "rejected" or "unavailable" or "failed")
            state = "rejected";
        var arrays = SliceArrays(preview.Arrays, index);
        var result = new DynamicWindowResultResponse(
            Identity(target.RecordingId), state, OutputKind(preview.Output),
            preview.Axes, AxisUnits(preview.AxisMetadata), arrays,
            ArrayUnits(preview.ArrayMetadata), quality, null, null, WindowProvenance(window));
        var startSample = Long(window, "start_sample");
        var endSample = Long(window, "end_sample");
        return new DynamicWindowResponse(
            $"full-cache:{preview.RunId}", $"full-cache:{preview.RunId}:{WindowId(window, index)}",
            target.RequestId ?? string.Empty, state, Identity(target.RecordingId), null,
            new DynamicWindowRange(startSample, endSample,
                startSample / rate, endSample / rate),
            quality, null, result);
    }

    public static DynamicWindowResponse ToDynamicWindowResponse(
        WindowPreviewResponse preview,
        DynamicWindowRequest target,
        double? samplingRateHz)
    {
        if (preview.Window is null)
            return new DynamicWindowResponse(
                $"full:{preview.RunId}", $"full:{preview.RunId}:{preview.CursorSample}",
                target.RequestId ?? string.Empty, MapState(preview.Status), Identity(preview.RecordingId),
                null, null, Quality(preview.Quality), Failure(preview), null);

        if (samplingRateHz is not > 0 || !double.IsFinite(samplingRateHz.Value))
            throw new InvalidOperationException("完整结果窗口缺少有效采样率，无法建立时间范围。");
        var rate = samplingRateHz.Value;
        var window = preview.Window;
        var startSeconds = window.StartSample / rate;
        var endSeconds = window.EndSample / rate;
        var state = MapState(preview.Status);
        var result = preview.Arrays is null || preview.Axes is null
            ? null
            : new DynamicWindowResultResponse(
                Identity(preview.RecordingId), state, OutputKind(preview.Output),
                NormalizeAxes(preview.Axes, preview.AxisMetadata),
                AxisUnits(preview.AxisMetadata), preview.Arrays,
                ArrayUnits(preview.ArrayMetadata), Quality(preview.Quality), FailureElement(preview),
                null, null);
        return new DynamicWindowResponse(
            $"full:{preview.RunId}", $"full:{preview.RunId}:{window.WindowId}",
            target.RequestId ?? string.Empty, state, Identity(preview.RecordingId), null,
            new DynamicWindowRange(window.StartSample, window.EndSample,
                startSeconds, endSeconds),
            Quality(preview.Quality), Failure(preview), result);
    }

    private static (int Index, JsonElement Window)? SelectWindow(
        IReadOnlyList<JsonElement> windows, long cursorSample)
    {
        (int Index, JsonElement Window)? selected = null;
        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            if (window.ValueKind != JsonValueKind.Object) continue;
            if (Long(window, "end_sample") <= cursorSample)
                selected = (index, window);
            else
                break;
        }
        return selected;
    }

    private static IReadOnlyDictionary<string, JsonElement> SliceArrays(
        IReadOnlyDictionary<string, JsonElement> arrays, int index)
    {
        var sliced = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in arrays)
        {
            if (item.Value.ValueKind == JsonValueKind.Array &&
                item.Value.GetArrayLength() > index &&
                item.Value[index].ValueKind == JsonValueKind.Array)
                sliced[item.Key] = item.Value[index];
            else
                sliced[item.Key] = item.Value;
        }
        return sliced;
    }

    private static string WindowState(JsonElement window) =>
        String(window, "state") switch
        {
            "Complete" => "ready",
            "Rejected" => "rejected",
            "Partial" => "unavailable",
            _ => "unavailable",
        };

    private static string WindowQuality(JsonElement window) =>
        String(window, "quality") ?? String(window, "quality_status") ?? "unknown";

    private static string WindowId(JsonElement window, int index) =>
        String(window, "window_id") ?? $"window-{index}";

    private static JsonElement? WindowProvenance(JsonElement window) =>
        window.TryGetProperty("provenance", out var provenance) &&
        provenance.ValueKind == JsonValueKind.Object
            ? provenance
            : null;

    private static long Long(JsonElement window, string name) =>
        window.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result
            : throw new InvalidOperationException($"完整结果窗口缺少 {name}。");

    private static string? String(JsonElement window, string name) =>
        window.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string MapState(string status) => status switch
    {
        "available" => "ready",
        "insufficient_data" => "insufficient_data",
        "rejected" => "rejected",
        "cancelled" => "cancelled",
        _ => "unavailable",
    };

    private static JsonElement Identity(string recordingId) =>
        JsonSerializer.SerializeToElement(new { recording_id = recordingId });

    private static string? OutputKind(JsonElement? output) =>
        output is { ValueKind: JsonValueKind.Object } value &&
        value.TryGetProperty("kind", out var kind) &&
        kind.ValueKind == JsonValueKind.String
            ? kind.GetString()
            : null;

    private static string? Quality(JsonElement? quality) =>
        quality is { ValueKind: JsonValueKind.String } value
            ? value.GetString()
            : quality is { } structured ? structured.GetRawText() : null;

    private static DynamicWindowFailure? Failure(WindowPreviewResponse preview)
    {
        if (preview.Status is "available" && preview.ReasonCodes.Count == 0) return null;
        var code = preview.ReasonCodes.FirstOrDefault() ?? "FULL_WINDOW_UNAVAILABLE";
        return new DynamicWindowFailure(code, string.Join("；", preview.ReasonCodes));
    }

    private static JsonElement? FailureElement(WindowPreviewResponse preview) =>
        Failure(preview) is { } failure
            ? JsonSerializer.SerializeToElement(failure)
            : null;

    private static IReadOnlyDictionary<string, string> AxisUnits(
        IReadOnlyDictionary<string, JsonElement>? metadata) =>
        metadata is null
            ? new Dictionary<string, string>()
            : metadata
                .Where(item => item.Value.ValueKind == JsonValueKind.Object &&
                    item.Value.TryGetProperty("unit", out _))
                .ToDictionary(item => item.Key, item => item.Value.GetProperty("unit").GetString() ?? string.Empty);

    private static IReadOnlyDictionary<string, JsonElement> NormalizeAxes(
        IReadOnlyDictionary<string, JsonElement> axes,
        IReadOnlyDictionary<string, JsonElement>? metadata)
    {
        if (metadata is null) return axes;
        var normalized = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in metadata)
        {
            if (item.Value.ValueKind == JsonValueKind.Object &&
                item.Value.TryGetProperty("array_key", out var key) &&
                key.ValueKind == JsonValueKind.String &&
                axes.TryGetValue(key.GetString()!, out var values))
            {
                normalized[item.Key] = values;
            }
        }
        return normalized.Count == 0 ? axes : normalized;
    }

    private static IReadOnlyDictionary<string, string> ArrayUnits(
        IReadOnlyDictionary<string, JsonElement>? metadata) =>
        metadata is null
            ? new Dictionary<string, string>()
            : metadata
                .Where(item => item.Value.ValueKind == JsonValueKind.Object &&
                    item.Value.TryGetProperty("unit", out _))
                .ToDictionary(item => item.Key, item => item.Value.GetProperty("unit").GetString() ?? string.Empty);
}
