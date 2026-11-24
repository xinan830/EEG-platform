using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Checks source compatibility without changing either algorithm's quality policy.</summary>
internal static class ReviewSpectralSources
{
    internal static readonly string[] Bands = ["delta", "theta", "alpha", "beta", "gamma"];

    public static void Validate(WindowPreviewResponse psd, WindowPreviewResponse rbp, IReadOnlyList<string> channels)
    {
        if (psd.Status != "available" || rbp.Status != "available")
            throw new InvalidOperationException($"PSD：{psd.Status} {string.Join("、", psd.ReasonCodes)}；" +
                $"RBP：{rbp.Status} {string.Join("、", rbp.ReasonCodes)}");
        if (channels.Count == 0 || channels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != channels.Count ||
            psd.RecordingId != rbp.RecordingId || psd.CursorSample != rbp.CursorSample ||
            psd.Window is not { } left || rbp.Window is not { } right ||
            left.StartSample != right.StartSample || left.EndSample != right.EndSample)
            throw new InvalidOperationException("PSD/RBP 记录、游标或实际样本窗口不匹配。");
        var psdContext = Context(psd, "psd");
        var rbpContext = Context(rbp, "rbp");
        foreach (var key in new[] { "sampling_rate_hz", "signal_mapping", "reference", "filters" })
            if (!psdContext.TryGetProperty(key, out var a) || !rbpContext.TryGetProperty(key, out var b) ||
                !ReviewJsonValueComparer.Equivalent(a, b))
                throw new InvalidOperationException($"PSD/RBP 科学配置 {key} 不匹配或缺失。");
        foreach (var key in new[] { "welch_segment_s", "welch_overlap", "dynamic_window_s", "refresh_step_s", "alignment" })
            if (!Required(psdContext, "window").TryGetProperty(key, out var a) ||
                !Required(rbpContext, "window").TryGetProperty(key, out var b) ||
                !ReviewJsonValueComparer.Equivalent(a, b))
                throw new InvalidOperationException($"PSD/RBP 分析窗口设置 {key} 不匹配或缺失。");
        var psdChannels = Required(Required(psdContext, "signal_mapping"), "channels")
            .EnumerateArray().Select(item => item.GetString()).ToArray();
        var rbpChannels = Required(Required(Required(rbp.Evidence ?? default, "extensions"), "channel_order"), "channels")
            .EnumerateArray().Select(item => item.GetString()).ToArray();
        if (!channels.SequenceEqual(psdChannels, StringComparer.Ordinal) || rbpChannels is null ||
            !channels.SequenceEqual(rbpChannels, StringComparer.Ordinal))
            throw new InvalidOperationException("PSD/RBP 返回的导联顺序与当前导联不一致。");
        if (Identity(psd, "source_sha256") != Identity(rbp, "source_sha256"))
            throw new InvalidOperationException("PSD/RBP 源数据身份不匹配。");
    }

    public static void ValidateProjection(ResultScalarsResponse response, WindowPreviewResponse window,
        IReadOnlyList<string> channels, string prefix, string unit)
    {
        if (response.AlgorithmId != Identity(window, "algorithm_id") || response.RecordingId != window.RecordingId ||
            response.WindowId != window.Window?.WindowId || response.Status is not ("ready" or "unavailable") ||
            response.AlgorithmVersion != window.ScientificVersion || response.ImplementationVersion != window.ImplementationVersion ||
            response.WindowStartSeconds != window.WindowStartSeconds || response.WindowEndSeconds != window.WindowEndSeconds ||
            !response.Fields.Select(field => field.FieldId).SequenceEqual(Bands.Select(band => prefix + band)))
            throw new InvalidOperationException("频段投影与当前算法窗口或请求字段不一致。");
        foreach (var field in response.Fields)
            if (field.Unit != unit || field.Status is not ("ready" or "unavailable") ||
                !field.Points.Select(point => point.Channel).SequenceEqual(channels, StringComparer.Ordinal))
                throw new InvalidOperationException("频段投影单位或导联顺序不一致。");
    }

    private static JsonElement Context(WindowPreviewResponse window, string algorithm)
    {
        if (Identity(window, "algorithm_id") != algorithm || string.IsNullOrWhiteSpace(window.ScientificConfigurationJson))
            throw new InvalidOperationException("算法结果缺少可核验的科学配置。");
        return ReviewScientificConfigurationSnapshot.Parse(window.ScientificConfigurationJson,
            Identity(window, "configuration_sha256")).ScientificContext;
    }

    private static JsonElement Required(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var property)
            ? property : throw new InvalidOperationException($"算法结果缺少科学配置或证据字段 {key}。");

    private static string Identity(WindowPreviewResponse window, string key) =>
        window.BackendIdentity is { ValueKind: JsonValueKind.Object } identity &&
        identity.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! :
        throw new InvalidOperationException($"算法结果缺少身份字段 {key}。");
}
