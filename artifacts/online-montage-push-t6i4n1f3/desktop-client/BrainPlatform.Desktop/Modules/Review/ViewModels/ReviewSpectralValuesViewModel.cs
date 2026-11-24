using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Numeric presentation only; scientific values belong to the existing PSD/RBP results.</summary>
public sealed class ReviewSpectralValuesViewModel : ObservableObject
{
    private string selectedBandId = "alpha";
    private string statusText = "等待当前 PSD/RBP 窗口。";
    private string windowText = "分析范围不可用";
    private string provenanceText = "";
    private IReadOnlyList<string> channels = [];
    private WindowPreviewResponse? psdWindow;
    private WindowPreviewResponse? rbpWindow;
    private ResultScalarsResponse? psdProjection;
    private ResultScalarsResponse? amplitudeProjection;

    public ObservableCollection<ReviewSpectralValueRow> Rows { get; } = [];
    public IReadOnlyList<PsdTopomapBandOption> Bands { get; } =
    [
        new("delta", "Delta 波 · 1–4 Hz"), new("theta", "Theta 波 · 4–8 Hz"),
        new("alpha", "Alpha 波 · 8–13 Hz"), new("beta", "Beta 波 · 13–30 Hz"),
        new("gamma", "Gamma 波 · 30–50 Hz"),
    ];
    public string SelectedBandId
    {
        get => selectedBandId;
        set { if (Bands.Any(band => band.Id == value) && SetProperty(ref selectedBandId, value)) UpdateRows(); }
    }
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string WindowText { get => windowText; private set => SetProperty(ref windowText, value); }
    public string ProvenanceText { get => provenanceText; private set => SetProperty(ref provenanceText, value); }

    public void InitializeChannels(IReadOnlyList<string> value)
    {
        if (!channels.SequenceEqual(value, StringComparer.Ordinal))
        {
            channels = value.ToArray();
            Rows.Clear();
            foreach (var channel in channels) Rows.Add(new(channel));
        }
        Clear("等待当前 PSD/RBP 窗口。");
    }

    public void Clear(string reason)
    {
        psdWindow = rbpWindow = null;
        psdProjection = amplitudeProjection = null;
        WindowText = "分析范围不可用";
        ProvenanceText = "";
        StatusText = reason;
        foreach (var row in Rows) row.Update(null, null, null, null, reason);
    }

    public void Apply(WindowPreviewResponse psd, WindowPreviewResponse rbp,
        ResultScalarsResponse density, ResultScalarsResponse amplitude)
    {
        ReviewSpectralSources.Validate(psd, rbp, channels);
        ReviewSpectralSources.ValidateProjection(density, psd, channels, "psd.band-mean-density.", "V^2/Hz");
        ReviewSpectralSources.ValidateProjection(amplitude, rbp, channels, "rbp.amplitude.", "uV");
        psdWindow = psd;
        rbpWindow = rbp;
        psdProjection = density;
        amplitudeProjection = amplitude;
        WindowText = $"{psd.WindowStartSeconds:0.###}–{psd.WindowEndSeconds:0.###} s · " +
            $"样本 [{psd.Window!.StartSample}, {psd.Window.EndSample})";
        using var document = JsonDocument.Parse(psd.ScientificConfigurationJson!);
        var context = document.RootElement.GetProperty("context");
        var settings = context.GetProperty("window");
        ProvenanceText = $"PSD：{psd.ScientificVersion} / {psd.Window.WindowId}\n" +
            $"RBP：{rbp.ScientificVersion} / {rbp.Window!.WindowId}\n" +
            $"分析窗口 {settings.GetProperty("dynamic_window_s")} s；Welch 分段 " +
            $"{settings.GetProperty("welch_segment_s")} s；重叠 {settings.GetProperty("welch_overlap")}\n" +
            $"参考：{context.GetProperty("reference")}\n滤波：{context.GetProperty("filters")}\n" +
            $"PSD 身份：{psd.BackendIdentity}\nRBP 身份：{rbp.BackendIdentity}";
        UpdateRows();
    }

    private void UpdateRows()
    {
        if (rbpWindow is null || psdProjection is null || amplitudeProjection is null) return;
        var density = psdProjection.Fields.Single(field => field.FieldId == $"psd.band-mean-density.{SelectedBandId}");
        var amplitude = amplitudeProjection.Fields.Single(field => field.FieldId == $"rbp.amplitude.{SelectedBandId}");
        var bandLabel = Bands.Single(band => band.Id == SelectedBandId).Label;
        for (var index = 0; index < channels.Count; index++)
        {
            var channel = channels[index];
            var reasons = new List<string>();
            var absolute = ReadRbp($"{SelectedBandId}_absolute_power", "uV^2", index, reasons);
            var relative = ReadRbp(SelectedBandId, "ratio", index, reasons);
            if (relative is > 1) { relative = null; reasons.Add("RBP 相对功率超出 ratio 范围。"); }
            var rms = ReadPoint(amplitude, index, reasons);
            if (absolute is null) rms = null;
            var psd = ReadPoint(density, index, reasons);
            var quality = ChannelQuality(rbpWindow.Evidence, channel);
            if (!quality.Available)
                absolute = relative = rms = null;
            var relativePercent = relative * 100;
            var densityUv2Hz = psd * 1e12;
            var details = $"{channel} · {bandLabel}\n" +
                $"PSD：{PointQuality(density, index)}\nRBP：{quality.Description}\n\n" +
                $"振幅 RMS：{FormatValue(rms)} µV\n绝对功率：{FormatValue(absolute)} µV²\n" +
                $"相对功率：{FormatValue(relativePercent)} %\n频段平均 PSD：{FormatValue(densityUv2Hz)} µV²/Hz\n" +
                string.Join("\n", reasons.Distinct());
            Rows[index].Update(rms, absolute, relativePercent, densityUv2Hz, details.TrimEnd());
        }
        StatusText = $"PSD：{psdWindow?.Window?.QualityStatus} · RBP：{rbpWindow.Window?.QualityStatus}；" +
            "各自保留原质量规则，悬停行查看质量和数值详情；— 表示不可用。";
    }

    private double? ReadRbp(string name, string unit, int index, List<string> reasons)
    {
        if (rbpWindow!.Arrays?.TryGetValue(name, out var array) != true ||
            rbpWindow.ArrayMetadata?.TryGetValue(name, out var metadata) != true ||
            metadata.ValueKind != JsonValueKind.Object || !metadata.TryGetProperty("unit", out var declared) ||
            declared.ValueKind != JsonValueKind.String || declared.GetString() != unit ||
            array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != channels.Count)
        { reasons.Add($"RBP {name} 缺失或单位/导联维度不匹配。"); return null; }
        var value = array[index];
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= 0)
            return number;
        reasons.Add($"RBP {name} 数值不可用。");
        return null;
    }

    private static double? ReadPoint(ResultScalarField field, int index, List<string> reasons)
    {
        var point = index < field.Points.Count ? field.Points[index] : null;
        if (point?.QualityStatus is "clean" or "warning" && point.Value is { } value && double.IsFinite(value) && value >= 0)
            return value;
        reasons.Add($"{field.Label}：{FailureDescription(field.Failure) ?? "数值不可用"}" +
            (point is null ? "" : $"；{DescribeQuality(point.QualityStatus, point.QualityReasons)}"));
        return null;
    }

    private static string PointQuality(ResultScalarField field, int index) => index < field.Points.Count
        ? DescribeQuality(field.Points[index].QualityStatus, field.Points[index].QualityReasons)
        : FailureDescription(field.Failure) ?? "不可用：缺少导联标量。";

    private static (bool Available, string Description) ChannelQuality(JsonElement? evidence, string channel)
    {
        if (evidence is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty("extensions", out var extensions) &&
            extensions.ValueKind == JsonValueKind.Object && extensions.TryGetProperty("channel_quality", out var quality) &&
            quality.ValueKind == JsonValueKind.Object && quality.TryGetProperty("by_channel", out var byChannel) &&
            byChannel.ValueKind == JsonValueKind.Object &&
            byChannel.TryGetProperty(channel, out var entry) && entry.ValueKind == JsonValueKind.Object &&
            entry.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
        {
            var reasons = entry.TryGetProperty("reasons", out var supplied) && supplied.ValueKind == JsonValueKind.Array
                ? supplied.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!).ToArray() : [];
            var description = DescribeQuality(status.GetString(), reasons);
            if (entry.TryGetProperty("clean_segments", out var clean) && clean.ValueKind == JsonValueKind.Number &&
                clean.TryGetInt32(out var cleanCount) && entry.TryGetProperty("total_segments", out var total) &&
                total.ValueKind == JsonValueKind.Number && total.TryGetInt32(out var totalCount))
                description += $"\n有效分段：{cleanCount}/{totalCount}";
            if (entry.TryGetProperty("peak_uv", out var peak) && peak.ValueKind == JsonValueKind.Number &&
                peak.TryGetDouble(out var peakUv) && double.IsFinite(peakUv))
                description += $"\n峰值：{peakUv:0.##} µV";
            return (status.GetString() is "clean" or "warning", description);
        }
        return (false, "不可用：缺少逐通道质量证据。");
    }

    private static string DescribeQuality(string? status, IEnumerable<string> reasons)
    {
        var label = status switch
        {
            "clean" => "正常（clean）", "warning" => "警告（warning）",
            "unavailable" => "不可用（unavailable）", "unknown" => "未知（unknown）",
            _ => $"未识别质量状态（{status ?? "未提供"}）",
        };
        var reason = string.Join("、", reasons.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrEmpty(reason) ? label : $"{label}；原因：{reason}";
    }

    private static string? FailureDescription(JsonElement? failure)
    {
        if (failure is not { ValueKind: JsonValueKind.Object } value) return null;
        var parts = new[] { "code", "message" }.Select(name =>
            value.TryGetProperty(name, out var part) && part.ValueKind == JsonValueKind.String ? part.GetString() : null);
        var description = string.Join("：", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrEmpty(description) ? null : description;
    }

    private static string FormatValue(double? value) => value?.ToString("G6", CultureInfo.CurrentUICulture) ?? "不可用";
}
