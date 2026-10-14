using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;

/// <summary>PSD quality reasons and evidence formatting; no scientific computation.</summary>
public sealed partial class PsdDetailViewModel
{
    private static string BuildFailureDetails(StructuredPreviewResponse? preview, StructuredRunError? runError)
    {
        if (preview is null || preview.Windows.Count == 0)
            return runError is not null
                ? BuildRunErrorDetails(runError)
                : preview?.Failure is { } failure && IsFailureObject(failure)
                    ? BuildStructuredFailureDetails(failure)
                    : "暂无失败或不可用窗口。";

        var failed = preview.Windows
            .Where(window => WindowState(window) is not "Complete")
            .ToArray();
        if (failed.Length == 0)
            return runError is not null
                ? BuildRunErrorDetails(runError)
                : preview.Failure is { } failure && IsFailureObject(failure)
                    ? BuildStructuredFailureDetails(failure)
                    : "暂无失败或不可用窗口。";

        var lines = new List<string>
        {
            $"问题窗口：{failed.Length} 个（共 {preview.Windows.Count} 个）",
            ""
        };

        var reasonCounts = failed
            .Select(ReasonCode)
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .GroupBy(reason => reason, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (reasonCounts.Length > 0)
        {
            lines.Add("原因统计：");
            lines.AddRange(reasonCounts.Select(group => $"• {AlgorithmResultFormatter.FormatFailureCodeForDisplay(group.Key)}：{group.Count()} 个"));
            lines.Add("");
        }

        var qualityReasons = failed
            .SelectMany(QualityReasons)
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .GroupBy(reason => reason, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (qualityReasons.Length > 0)
        {
            lines.Add("质量门原因：");
            lines.AddRange(qualityReasons.Select(group => $"• {AlgorithmResultFormatter.FormatQualityReasonForDisplay(group.Key)}：{group.Count()} 个"));
            lines.Add("");
        }

        lines.Add("窗口明细：");
        foreach (var window in failed.Take(100))
        {
            var reason = ReasonCode(window);
            var quality = string.Join("、", QualityReasons(window));
            var detail = string.Join("；", new[]
            {
                AlgorithmResultFormatter.FormatWindowStateForDisplay(WindowState(window)),
                string.IsNullOrWhiteSpace(reason) ? null : $"原因 {AlgorithmResultFormatter.FormatFailureCodeForDisplay(reason)}",
                string.IsNullOrWhiteSpace(quality) ? null : $"质量门 {AlgorithmResultFormatter.FormatQualityReasonForDisplay(quality)}"
            }.Where(value => value is not null));
            lines.Add($"• {Number(window, "start_s"):0.###}–{Number(window, "end_s"):0.###} 秒：{detail}");
        }
        if (failed.Length > 100)
            lines.Add($"• 其余 {failed.Length - 100} 个窗口未展开。");
        return string.Join(Environment.NewLine, lines);
    }

    private static string? StructuredFailureSummary(JsonElement? failure)
    {
        if (failure is not { } value || !IsFailureObject(value))
            return null;
        var code = value.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
        var message = value.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
        return $"{AlgorithmResultFormatter.FormatFailureCodeForDisplay(code ?? "")}{(string.IsNullOrWhiteSpace(message) ? "" : $"：{message}")}";
    }

    private static string BuildStructuredFailureDetails(JsonElement failure)
    {
        var lines = new List<string>();
        var code = failure.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
        var message = failure.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
        if (!string.IsNullOrWhiteSpace(code))
            lines.Add($"错误代码：{AlgorithmResultFormatter.FormatFailureCodeForDisplay(code)}（{code}）");
        if (!string.IsNullOrWhiteSpace(message))
            lines.Add($"错误信息：{message}");
        if (failure.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.Object)
        {
            lines.Add("");
            lines.Add("质量证据：");
            AddNumber(detail, lines, "clean_segments", "有效片段数");
            AddNumber(detail, lines, "total_segments", "候选片段数");
            if (detail.TryGetProperty("clean_ratio", out var ratio) && ratio.TryGetDouble(out var cleanRatio))
                lines.Add($"• 有效比例：{cleanRatio:P1}");
            AddAmplitudeEvidence(lines, detail);
            if (detail.TryGetProperty("gate_failed", out var gate) && gate.ValueKind == JsonValueKind.String)
                lines.Add($"• 质量门判定：{AlgorithmResultFormatter.FormatQualityReasonForDisplay(gate.GetString() ?? "")}");
        }
        return lines.Count == 0 ? "结构化结果未提供失败详情。" : string.Join(Environment.NewLine, lines);
    }

    private static bool IsFailureObject(JsonElement? value) => value is { ValueKind: JsonValueKind.Object };

    private static string BuildRunErrorDetails(StructuredRunError error)
    {
        var lines = new List<string>
        {
            $"错误代码：{AlgorithmResultFormatter.FormatFailureCodeForDisplay(error.Code)}（{error.Code}）",
            $"错误信息：{error.Message}"
        };
        if (!string.IsNullOrWhiteSpace(error.Stage))
            lines.Add($"失败阶段：{error.Stage}");

        if (error.Details is not { } details || details.ValueKind != JsonValueKind.Object)
            return string.Join(Environment.NewLine, lines);

        if (details.TryGetProperty("quality", out var quality) && quality.ValueKind == JsonValueKind.Object)
        {
            lines.Add("");
            lines.Add("质量证据：");
            AddNumber(quality, lines, "clean_segments", "有效片段数");
            AddNumber(quality, lines, "total_segments", "候选片段数");
            if (quality.TryGetProperty("clean_ratio", out var ratio) && ratio.TryGetDouble(out var cleanRatio))
                lines.Add($"• 有效比例：{cleanRatio:P1}");
            AddAmplitudeEvidence(lines, quality);
            if (quality.TryGetProperty("gate_failed", out var gate) && gate.ValueKind == JsonValueKind.String)
                lines.Add($"• 质量门判定：{AlgorithmResultFormatter.FormatQualityReasonForDisplay(gate.GetString() ?? "")}");
            if (quality.TryGetProperty("rejected_reasons", out var reasons) && reasons.ValueKind == JsonValueKind.Array)
            {
                var labels = reasons.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => AlgorithmResultFormatter.FormatQualityReasonForDisplay(item.GetString() ?? ""))
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .ToArray();
                if (labels.Length > 0)
                    lines.Add($"• 拒绝原因：{string.Join("、", labels)}");
            }
        }
        else
        {
            lines.Add($"错误详情：{details}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddAmplitudeEvidence(List<string> lines, JsonElement quality)
    {
        var amplitude = quality.TryGetProperty("amplitude", out var direct) && direct.ValueKind == JsonValueKind.Object
            ? direct
            : quality.TryGetProperty("evidence", out var evidence) && evidence.ValueKind == JsonValueKind.Object
                && evidence.TryGetProperty("amplitude", out var evidenceAmplitude) && evidenceAmplitude.ValueKind == JsonValueKind.Object
                    ? evidenceAmplitude
                    : quality.TryGetProperty("evidence", out evidence) && evidence.ValueKind == JsonValueKind.Object
                        && evidence.TryGetProperty("spectral_evidence", out var spectral) && spectral.ValueKind == JsonValueKind.Object
                        && spectral.TryGetProperty("amplitude", out var nested) && nested.ValueKind == JsonValueKind.Object
                            ? nested
                            : default;
        if (amplitude.ValueKind != JsonValueKind.Object)
            return;

        if (!TryGetDouble(amplitude, "peak_uv", out var peak) || !TryGetDouble(amplitude, "threshold_uv", out var threshold))
            return;
        var exceeded = TryGetDouble(amplitude, "exceeded_uv", out var amount)
            ? amount
            : Math.Max(0, peak - threshold);
        lines.Add($"• 峰值振幅：{peak:0.###} μV");
        lines.Add($"• 质量阈值：{threshold:0.###} μV");
        if (exceeded > 0)
            lines.Add($"• 超出阈值：{exceeded:0.###} μV");
    }

    private static bool TryGetDouble(JsonElement value, string propertyName, out double number)
    {
        number = 0;
        return value.TryGetProperty(propertyName, out var element)
            && element.TryGetDouble(out number)
            && double.IsFinite(number);
    }

    private static void AddNumber(JsonElement value, List<string> lines, string propertyName, string label)
    {
        if (value.TryGetProperty(propertyName, out var element) && element.TryGetInt32(out var number))
            lines.Add($"• {label}：{number}");
    }

    private static string GetWindowReasonSummary(StructuredPreviewResponse? preview)
    {
        if (preview is null)
            return "无";

        var problemWindows = preview.Windows.Where(window => WindowState(window) is not "Complete").ToArray();
        if (problemWindows.Length == 0)
            return "无";

        var reasons = problemWindows
            .SelectMany(window => new[] { ReasonCode(window) }.Concat(QualityReasons(window)))
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .GroupBy(reason => reason, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{AlgorithmResultFormatter.FormatFailureCodeForDisplay(group.Key)}（{group.Count()} 个）")
            .ToArray();

        return reasons.Length > 0
            ? string.Join("、", reasons)
            : $"有 {problemWindows.Length} 个窗口未完成，请查看窗口失败原因";
    }

    private static string WindowState(JsonElement window) =>
        window.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String
            ? state.GetString() ?? "未知"
            : "未知";

    private static string ReasonCode(JsonElement window)
    {
        if (window.TryGetProperty("failure", out var failure) &&
            failure.ValueKind == JsonValueKind.Object &&
            failure.TryGetProperty("code", out var code) &&
            code.ValueKind == JsonValueKind.String)
            return code.GetString() ?? "";
        return "";
    }

    private static IEnumerable<string> QualityReasons(JsonElement window)
    {
        if (!window.TryGetProperty("evidence", out var evidence) ||
            evidence.ValueKind != JsonValueKind.Object ||
            !evidence.TryGetProperty("source_quality", out var quality) ||
            quality.ValueKind != JsonValueKind.Object ||
            !quality.TryGetProperty("rejected_reasons", out var reasons) ||
            reasons.ValueKind != JsonValueKind.Array)
            return [];
        return reasons.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .Where(item => item.Length > 0);
    }

    private static double Number(JsonElement value, string name) =>
        value.TryGetProperty(name, out var element) && element.TryGetDouble(out var number) ? number : double.NaN;
}
