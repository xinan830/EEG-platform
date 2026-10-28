using System.Text.Json;
using System.Runtime.CompilerServices;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

/// <summary>
/// Converts backend-owned Full Run window data into the common single-window
/// preview contract. No EEG calculation is performed here.
/// </summary>
internal static class FullRunWindowPreviewAdapter
{
    private static readonly ConditionalWeakTable<StructuredPreviewResponse, FullRunWindowIndex> WindowIndexes = [];

    public static DynamicWindowResponse ToDynamicWindowResponse(
        StructuredPreviewResponse preview,
        DynamicWindowRequest target,
        double? samplingRateHz)
    {
        if (samplingRateHz is not > 0 || !double.IsFinite(samplingRateHz.Value))
            throw new InvalidOperationException("完整结果窗口缺少有效采样率，无法建立时间范围。");
        var selected = WindowIndexes.GetValue(preview, item => FullRunWindowIndex.Create(item.Windows))
            .Find(target.CursorSample);
        if (selected is null)
        {
            return new DynamicWindowResponse(
                $"full-cache:{preview.RunId}", $"full-cache:{preview.RunId}:{target.CursorSample}",
                target.RequestId ?? string.Empty, "insufficient_data", Identity(target.RecordingId),
                null, null, "unavailable",
                new DynamicWindowFailure("NO_COMPLETED_WINDOW", "完整结果没有覆盖当前游标的窗口"), null);
        }

        var rate = samplingRateHz.Value;
        var descriptor = FullRunResultDescriptors.For(preview.Output);
        descriptor.Validate(preview);
        var index = selected.Value.Index;
        var window = selected.Value.Window;
        var state = WindowState(window);
        var quality = WindowQuality(window);
        if (quality is "rejected" or "unavailable" or "failed")
            state = quality is "rejected" ? "rejected" : "unavailable";
        var failure = WindowFailure(window);
        if (state == "ready" && !HasCurrentContract(window, preview.Output))
        {
            state = "unavailable";
            quality = "unavailable";
            failure = ContractFailure();
        }
        var result = state != "ready" ? null : new DynamicWindowResultResponse(
            Identity(target.RecordingId), state, OutputKind(preview.Output),
            WindowAxes(preview, index, descriptor), AxisUnits(preview.AxisMetadata),
            SliceArrays(preview.Arrays, index, descriptor.OutputArrays),
            ArrayUnits(preview.ArrayMetadata, descriptor.OutputArrays), quality, null,
            WindowEvidence(preview, window), WindowProvenance(window, preview.Output));
        var startSample = Long(window, "start_sample");
        var endSample = Long(window, "end_sample");
        return new DynamicWindowResponse(
            $"full-cache:{preview.RunId}", $"full-cache:{preview.RunId}:{WindowId(window, index)}",
            target.RequestId ?? string.Empty, state, Identity(target.RecordingId), null,
            new DynamicWindowRange(startSample, endSample,
                startSample / rate, endSample / rate),
            quality, failure, result);
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
        var descriptor = FullRunResultDescriptors.For(preview.Output);
        var windowEvidence = JsonSerializer.SerializeToElement(new
        {
            state = window.WindowState, provenance = window.Provenance,
        });
        var missingContract = state == "ready" && !HasCurrentContract(windowEvidence, preview.Output);
        if (missingContract) state = "unavailable";
        var result = state != "ready" || preview.Arrays is null || preview.Axes is null
            ? null
            : new DynamicWindowResultResponse(
                Identity(preview.RecordingId), state, OutputKind(preview.Output),
                NormalizeAxes(preview.Axes, preview.AxisMetadata),
                AxisUnits(preview.AxisMetadata), preview.Arrays,
                ArrayUnits(preview.ArrayMetadata, descriptor.OutputArrays), Quality(preview.Quality), FailureElement(preview),
                null, WindowProvenance(windowEvidence, preview.Output));
        return new DynamicWindowResponse(
            $"full:{preview.RunId}", $"full:{preview.RunId}:{window.WindowId}",
            target.RequestId ?? string.Empty, state, Identity(preview.RecordingId), null,
            new DynamicWindowRange(window.StartSample, window.EndSample,
                startSeconds, endSeconds),
            missingContract ? "unavailable" : Quality(preview.Quality),
            missingContract ? ContractFailure() : Failure(preview), result);
    }

    private static IReadOnlyDictionary<string, JsonElement> SliceArrays(
        IReadOnlyDictionary<string, JsonElement> arrays, int index, IReadOnlyList<string> outputArrays)
    {
        var sliced = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var name in outputArrays)
        {
            if (arrays.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Array &&
                value.GetArrayLength() > index && value[index].ValueKind == JsonValueKind.Array)
                sliced[name] = value[index];
            else
                throw new InvalidOperationException($"完整结果缺少算法输出数组 {name} 的对应窗口行。");
        }
        return sliced;
    }

    private static IReadOnlyDictionary<string, JsonElement> WindowAxes(
        StructuredPreviewResponse preview, int index, FullRunResultDescriptor descriptor)
    {
        if (descriptor.Kind != "algorithm_group") return preview.Axes;
        var axes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["time_s"] = JsonSerializer.SerializeToElement(new[] { preview.Axes["time_s"][index].GetDouble() }),
            ["channel_index"] = preview.Axes["channel_index"],
        };
        return axes;
    }

    private static JsonElement? WindowEvidence(StructuredPreviewResponse preview, JsonElement window)
    {
        var output = preview.Output;
        if (OutputKind(output) == "algorithm_group")
        {
            var channelFailures = window.TryGetProperty("evidence", out var evidence) && evidence.ValueKind == JsonValueKind.Object &&
                evidence.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object &&
                extensions.TryGetProperty("channel_failures", out var failures)
                    ? failures
                    : JsonSerializer.SerializeToElement(new { by_channel = new Dictionary<string, object>() });
            return JsonSerializer.SerializeToElement(new { extensions = new
            {
                channel_order = new { channels = preview.ChannelOrder },
                channel_failures = channelFailures,
            }});
        }
        return null;
    }

    private static string WindowState(JsonElement window) =>
        String(window, "state") switch
        {
            "Complete" => "ready",
            "Rejected" => "rejected",
            "Partial" => "ready",
            _ => "unavailable",
        };

    private static string WindowQuality(JsonElement window) =>
        String(window, "quality") ?? String(window, "quality_status") ?? "unknown";

    private static string WindowId(JsonElement window, int index) =>
        String(window, "window_id") ?? $"window-{index}";

    private static JsonElement WindowProvenance(JsonElement window, JsonElement? output)
    {
        var fields = new Dictionary<string, JsonElement>();
        if (window.TryGetProperty("provenance", out var provenance) && provenance.ValueKind == JsonValueKind.Object)
            foreach (var item in provenance.EnumerateObject()) fields[item.Name] = item.Value;

        var analysisState = String(window, "state") ?? "Complete";
        fields["analysis_state"] = JsonSerializer.SerializeToElement(analysisState);
        return JsonSerializer.SerializeToElement(fields);
    }

    private static bool HasCurrentContract(JsonElement window, JsonElement? output) =>
        OutputKind(output) != "time_frequency" ||
        (window.TryGetProperty("provenance", out var provenance) && provenance.ValueKind == JsonValueKind.Object &&
            String(provenance, "preprocessing_scope") == "whole_recording_before_window_slice" &&
            String(provenance, "inner_time_axis") == "relative_window_time_center_s");

    private static DynamicWindowFailure ContractFailure() => new(
        "FULL_WINDOW_CONTRACT_UNAVAILABLE", "完整结果缺少当前算法所需的窗口证据，请重新计算。");

    private static DynamicWindowFailure? WindowFailure(JsonElement window) =>
        window.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object
            ? new DynamicWindowFailure(String(failure, "code") ?? "FULL_WINDOW_UNAVAILABLE",
                String(failure, "message") ?? "完整结果窗口不可用", failure)
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
        IReadOnlyDictionary<string, JsonElement>? metadata, IReadOnlyList<string> outputArrays) =>
        metadata is null
            ? new Dictionary<string, string>()
            : outputArrays
                .Where(name => metadata.TryGetValue(name, out var item) && item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("unit", out _))
                .ToDictionary(name => name, name => metadata[name].GetProperty("unit").GetString() ?? string.Empty);
}
