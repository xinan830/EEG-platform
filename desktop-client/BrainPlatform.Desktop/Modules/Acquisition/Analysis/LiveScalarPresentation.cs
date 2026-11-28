using System.Globalization;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Acquisition.Runtime;

namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

public sealed record LiveScalarPoint(
    double TimeSeconds,
    double? Value,
    string State,
    string Quality,
    double? WindowStartSeconds,
    double? WindowEndSeconds,
    string? FailureCode,
    string? Message,
    string? EstimateSource = null,
    double? PeakHz = null,
    double? CogHz = null,
    bool SplitPeak = false);

public sealed record LiveScalarUpdate(
    string AlgorithmId,
    string State,
    double? TimeSeconds,
    double? Value,
    string Unit,
    string Quality,
    double? StartSeconds,
    double? EndSeconds,
    string? ScientificIdentity,
    string? FailureCode,
    string? Message,
    string? FrequencyMode,
    double? IapfHz,
    string? DetailSummary,
    Guid? SessionId,
    long Revision,
    string? EstimateSource = null,
    double? PeakHz = null,
    double? CogHz = null,
    bool SplitPeak = false,
    string? ProcessingSummary = null);

/// <summary>Owns live scalar configuration serialization, result validation, and Fz/Pz channel role mapping.</summary>
internal static class LiveScalarPresentation
{
    internal static JsonElement Config(
        string algorithmId,
        IReadOnlyList<string> channels,
        double windowSeconds = 20d,
        double stepSeconds = 1d,
        double? notchHz = 50d,
        string frequencyMode = "standard") => JsonSerializer.SerializeToElement(new
    {
        schema_version = "live-scalar-config-v1",
        channels,
        window_s = windowSeconds,
        step_s = stepSeconds,
        notch_hz = notchHz,
        frequency_mode = string.Equals(algorithmId, "theta_beta", StringComparison.OrdinalIgnoreCase)
            ? frequencyMode
            : "standard",
    });

    internal static LiveScalarUpdate Read(
        LiveAnalysisUpdate update,
        IReadOnlyList<string> expectedChannels,
        string expectedUnit)
    {
        if (update.Payload.ValueKind != JsonValueKind.Object)
        {
            if (update.State == "warming_up")
            {
                return new(update.AlgorithmId, "warming_up", null, null, expectedUnit, "warming_up",
                    null, null, update.ScientificIdentity, null, null, null, null, null,
                    update.SessionId, update.Revision);
            }
            return new(update.AlgorithmId, "unavailable", update.EndSeconds, null, expectedUnit, "unavailable",
                update.StartSeconds, update.EndSeconds, update.ScientificIdentity,
                update.FailureCode, update.Message ?? "当前实时窗口不可用。",
                null, null, null, update.SessionId, update.Revision);
        }

        var payload = update.Payload;
        if (Text(payload, "type") != "live_scalar_result" ||
            Text(payload, "schema_version") != "live-scalar-result-v1" ||
            !string.Equals(Text(payload, "algorithm_id"), update.AlgorithmId, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(Text(payload, "acquisition_session_id"), out var payloadSession) ||
            payloadSession != update.SessionId ||
            Text(payload, "unit") != expectedUnit ||
            Text(payload, "state") != update.State ||
            Text(payload, "scientific_identity") != update.ScientificIdentity)
        {
            throw new InvalidOperationException("实时标量结果版本、身份或单位不匹配。");
        }

        var payloadChannels = payload.GetProperty("channels").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
        if (!payloadChannels.SequenceEqual(expectedChannels, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("实时标量结果通道顺序不匹配。");

        var startSeconds = payload.TryGetProperty("start_s", out var startEl) && startEl.ValueKind == JsonValueKind.Number
            ? startEl.GetDouble() : update.StartSeconds;
        var endSeconds = payload.TryGetProperty("end_s", out var endEl) && endEl.ValueKind == JsonValueKind.Number
            ? endEl.GetDouble() : update.EndSeconds;
        var timeSeconds = payload.TryGetProperty("time_s", out var timeEl) && timeEl.ValueKind == JsonValueKind.Number
            ? timeEl.GetDouble() : endSeconds;
        if (startSeconds != update.StartSeconds || endSeconds != update.EndSeconds)
            throw new InvalidOperationException("实时标量结果时间范围与外层消息不一致。");

        double? value = null;
        if (payload.TryGetProperty("value", out var valueEl) && valueEl.ValueKind == JsonValueKind.Number)
        {
            var number = valueEl.GetDouble();
            if (!double.IsFinite(number))
                throw new InvalidOperationException("实时标量结果包含非有限数值。");
            value = number;
        }
        if (update.State == "ready" && value is null)
            throw new InvalidOperationException("就绪状态的实时标量结果缺少有效数值。");

        var quality = Text(payload, "quality");
        string? failureCode = update.FailureCode;
        string? failureMessage = update.Message;
        if (payload.TryGetProperty("failure", out var failureEl) && failureEl.ValueKind == JsonValueKind.Object)
        {
            failureCode = Text(failureEl, "code") is { Length: > 0 } code ? code : failureCode;
            failureMessage = Text(failureEl, "message") is { Length: > 0 } msg ? msg : failureMessage;
        }

        var (frequencyMode, iapfHz, estimateSource, peakHz, cogHz, splitPeak, detailSummary) =
            ReadEvidenceSummary(payload, update.AlgorithmId, expectedChannels);
        var processingSummary = ReadProcessingSummary(payload);
        if (!string.IsNullOrWhiteSpace(processingSummary))
            detailSummary = string.IsNullOrWhiteSpace(detailSummary)
                ? processingSummary
                : $"{detailSummary} · {processingSummary}";
        return new(
            update.AlgorithmId,
            update.State,
            timeSeconds,
            value,
            expectedUnit,
            quality,
            startSeconds,
            endSeconds,
            update.ScientificIdentity,
            failureCode,
            failureMessage,
            frequencyMode,
            iapfHz,
            detailSummary,
            update.SessionId,
            update.Revision,
            estimateSource,
            peakHz,
            cogHz,
            splitPeak,
            processingSummary);
    }

    internal static (string? Primary, string? Secondary) ResolveBrainbeatChannels(
        IReadOnlyList<string> availableChannels,
        LiveMontageSnapshot? montage,
        string? currentPrimary = null,
        string? currentSecondary = null)
    {
        var primaryOptions = BrainbeatChannelOptions(availableChannels, montage, "Fz");
        var secondaryOptions = BrainbeatChannelOptions(availableChannels, montage, "Pz");
        return (ResolveSelectedRole(primaryOptions, currentPrimary), ResolveSelectedRole(secondaryOptions, currentSecondary));
    }

    internal static IReadOnlyList<string> BrainbeatChannelOptions(
        IReadOnlyList<string> availableChannels,
        LiveMontageSnapshot? montage,
        string role)
    {
        var supported = role.Equals("Fz", StringComparison.OrdinalIgnoreCase)
            ? new HashSet<string>(["Fz", "Fz-REF", "Fz-AVG"], StringComparer.OrdinalIgnoreCase)
            : role.Equals("Pz", StringComparison.OrdinalIgnoreCase)
                ? new HashSet<string>(["Pz", "Pz-REF", "Pz-AVG"], StringComparer.OrdinalIgnoreCase)
                : throw new ArgumentOutOfRangeException(nameof(role));
        return availableChannels.Where(name =>
        {
            if (!supported.Contains(name.Trim())) return false;
            if (montage is null) return true;
            var output = montage.Profile.DerivedChannels.FirstOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            return output is not null &&
                string.Equals(output.PositiveLabel, role, StringComparison.OrdinalIgnoreCase) &&
                (output.NegativeKind == MontageNegativeKind.OriginalHardwareReference && output.NegativeLabels.Count == 0 ||
                 output.NegativeKind == MontageNegativeKind.Mean && output.NegativeLabels.Count >= 2 &&
                 output.NegativeLabels.Distinct(StringComparer.OrdinalIgnoreCase).Count() == output.NegativeLabels.Count);
        }).ToArray();
    }

    internal static string? ResolveSingleChannel(
        IReadOnlyList<string> availableChannels,
        LiveMontageSnapshot? montage,
        string? currentChannel)
    {
        if (availableChannels.Count == 0)
            return null;
        if (montage is not null && !string.IsNullOrWhiteSpace(currentChannel))
        {
            var matched = montage.Profile.DerivedChannels.FirstOrDefault(output =>
                string.Equals(output.Name, currentChannel, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(output.PositiveLabel, currentChannel, StringComparison.OrdinalIgnoreCase))?.Name;
            if (matched is not null && availableChannels.Contains(matched, StringComparer.OrdinalIgnoreCase))
                return availableChannels.First(ch => string.Equals(ch, matched, StringComparison.OrdinalIgnoreCase));
        }
        return availableChannels.FirstOrDefault(ch => string.Equals(ch, currentChannel, StringComparison.OrdinalIgnoreCase))
            ?? availableChannels[0];
    }

    private static string? ResolveSelectedRole(IReadOnlyList<string> options, string? current) =>
        options.FirstOrDefault(name => string.Equals(name, current, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();

    private static (string? FrequencyMode, double? IapfHz, string? EstimateSource, double? PeakHz, double? CogHz, bool SplitPeak, string? DetailSummary) ReadEvidenceSummary(
        JsonElement payload,
        string algorithmId,
        IReadOnlyList<string> channels)
    {
        if (!payload.TryGetProperty("evidence", out var evidence) || evidence.ValueKind != JsonValueKind.Object ||
            !evidence.TryGetProperty("extensions", out var extensions) || extensions.ValueKind != JsonValueKind.Object)
        {
            return (null, null, null, null, null, false, algorithmId == "brainbeat" && channels.Count == 2 ? $"{channels[0]} / {channels[1]}" : null);
        }

        if (string.Equals(algorithmId, "theta_beta", StringComparison.OrdinalIgnoreCase) &&
            extensions.TryGetProperty("theta_beta_evidence", out var tb) && tb.ValueKind == JsonValueKind.Object)
        {
            var mode = Text(tb, "frequency_mode");
            double? iapf = tb.TryGetProperty("iapf_hz", out var iapfEl) && iapfEl.ValueKind == JsonValueKind.Number
                ? iapfEl.GetDouble() : null;
            var summary = mode == "individualized"
                ? iapf is { } hz ? $"IAF 相对 · IAPF {hz.ToString("0.##", CultureInfo.InvariantCulture)} Hz" : "IAF 相对"
                : "标准频段";
            return (mode, iapf, null, null, null, false, summary);
        }

        if (string.Equals(algorithmId, "iapf", StringComparison.OrdinalIgnoreCase) &&
            extensions.TryGetProperty("iapf_evidence", out var iapfEv) && iapfEv.ValueKind == JsonValueKind.Object)
        {
            var source = Text(iapfEv, "source");
            double? peakHz = Number(iapfEv, "peak_hz");
            double? cogHz = Number(iapfEv, "cog_hz");
            var splitPeak = iapfEv.TryGetProperty("split_peak", out var splitEl) && splitEl.ValueKind == JsonValueKind.True;
            var summary = Modules.Algorithms.Views.Shared.ScalarTrendChartStyle.FormatIapfSummary(source, peakHz, cogHz, splitPeak);
            return (null, null, string.IsNullOrEmpty(source) ? null : source, peakHz, cogHz, splitPeak, summary);
        }

        if (string.Equals(algorithmId, "brainbeat", StringComparison.OrdinalIgnoreCase) && channels.Count == 2)
            return (null, null, null, null, null, false, $"{channels[0]} / {channels[1]}");

        return (null, null, null, null, null, false, null);
    }

    private static double? Number(JsonElement element, string key) =>
        element.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Number &&
        item.TryGetDouble(out var value) && double.IsFinite(value)
            ? value
            : null;

    private static string? ReadProcessingSummary(JsonElement payload)
    {
        if (!payload.TryGetProperty("provenance", out var provenance) || provenance.ValueKind != JsonValueKind.Object ||
            Text(provenance, "preprocessing_scope") != "window_local_zero_phase" ||
            !provenance.TryGetProperty("equivalent_to_offline_continuous_preprocessing", out var equivalent) ||
            equivalent.ValueKind != JsonValueKind.False)
            return null;

        var band = provenance.TryGetProperty("filter_bandpass_hz", out var bandEl) &&
            bandEl.ValueKind == JsonValueKind.Array && bandEl.GetArrayLength() == 2
            ? $"{bandEl[0].GetDouble().ToString("0.##", CultureInfo.InvariantCulture)}–{bandEl[1].GetDouble().ToString("0.##", CultureInfo.InvariantCulture)} Hz"
            : "科学滤波";
        var notchText = "关闭陷波";
        if (provenance.TryGetProperty("notch_hz", out var notch) && notch.ValueKind == JsonValueKind.Number)
        {
            notchText = $"陷波 {notch.GetDouble().ToString("0.##", CultureInfo.InvariantCulture)} Hz";
            if (provenance.TryGetProperty("notch_quality_factor", out var quality) && quality.ValueKind == JsonValueKind.Number)
                notchText += $" (Q={quality.GetDouble().ToString("0.##", CultureInfo.InvariantCulture)})";
        }
        return $"实时窗内零相位 {band}；{notchText}；与回溯连续预处理不等价";
    }

    private static string Text(JsonElement value, string key) =>
        value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString() ?? string.Empty
            : string.Empty;
}
