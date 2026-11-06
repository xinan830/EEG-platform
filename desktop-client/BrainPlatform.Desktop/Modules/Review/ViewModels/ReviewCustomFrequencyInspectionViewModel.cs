using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public enum ReviewCustomFrequencyMode
{
    SingleFrequency,
    Range,
}

public sealed record ReviewCustomFrequencyCurvePoint(double FrequencyHz, double? DensityUv2PerHz);

public sealed class ReviewCustomFrequencyInspectionViewModel : ObservableObject
{
    private IReadOnlyList<string> channels = [];
    private CustomFrequencyBinsResponse? bins;
    private ReviewCustomFrequencyMode mode;
    private double selectedFrequencyHz = 10;
    private double rangeLowHz = 1;
    private double rangeHighHz = 3;
    private string? selectedChannel;
    private IReadOnlyList<ReviewCustomFrequencyCurvePoint> psdCurve = [];
    private string statusText = "等待当前 PSD/RBP 窗口。";
    private string windowText = "分析范围不可用";
    private string provenanceText = "";

    public ObservableCollection<ReviewCustomFrequencyRow> Rows { get; } = [];
    public bool HasBins => bins is not null;
    internal string? PsdWindowId => bins?.PsdProvenance.WindowId;
    internal string? RbpWindowId => bins?.RbpProvenance.WindowId;
    public double MinimumFrequencyHz => bins?.FrequencyHz[0] ?? 1;
    public double MaximumFrequencyHz => bins?.FrequencyHz[^1] ?? 50;
    public IReadOnlyList<double> FrequenciesHz => bins?.FrequencyHz ?? [];
    public IReadOnlyList<CustomFrequencyBinInterval> BinIntervals => bins?.BinIntervals ?? [];
    public IReadOnlyList<ReviewCustomFrequencyCurvePoint> PsdCurve
    {
        get => psdCurve;
        private set => SetProperty(ref psdCurve, value);
    }
    public ReviewCustomFrequencyMode Mode
    {
        get => mode;
        set
        {
            if (!SetProperty(ref mode, value)) return;
            RaisePropertyChanged(nameof(SelectedFrequencyHz));
            RaisePropertyChanged(nameof(ActualFrequencyHz));
            if (mode == ReviewCustomFrequencyMode.SingleFrequency) UpdatePointRows();
            else ClearRows("正在等待自定义范围投影。");
        }
    }
    public double SelectedFrequencyHz
    {
        get => selectedFrequencyHz;
        set => SelectFrequency(value);
    }
    public bool IsFrequencyValid => double.IsFinite(selectedFrequencyHz) &&
        selectedFrequencyHz >= MinimumFrequencyHz && selectedFrequencyHz <= MaximumFrequencyHz;
    public double? ActualFrequencyHz => bins is null || !IsFrequencyValid ? null : bins.FrequencyHz[FindNearestBin(selectedFrequencyHz)];
    public int? SelectedBinIndex => bins is null || !IsFrequencyValid ? null : FindNearestBin(selectedFrequencyHz);
    public double RangeLowHz
    {
        get => rangeLowHz;
        set { if (SetProperty(ref rangeLowHz, SnapToCurrentAxis(value))) OnRangeChanged(); }
    }
    public double RangeHighHz
    {
        get => rangeHighHz;
        set { if (SetProperty(ref rangeHighHz, SnapToCurrentAxis(value))) OnRangeChanged(); }
    }
    public bool IsRangeValid => double.IsFinite(RangeLowHz) && double.IsFinite(RangeHighHz) &&
        RangeLowHz >= MinimumFrequencyHz && RangeHighHz <= MaximumFrequencyHz && RangeHighHz > RangeLowHz;

    internal void SetRange(double lowHz, double highHz)
    {
        var low = SnapToCurrentAxis(lowHz);
        var high = SnapToCurrentAxis(highHz);
        var lowChanged = low != rangeLowHz;
        var highChanged = high != rangeHighHz;
        if (!lowChanged && !highChanged) return;
        rangeLowHz = low;
        rangeHighHz = high;
        OnRangeChanged();
        if (lowChanged) RaisePropertyChanged(nameof(RangeLowHz));
        if (highChanged) RaisePropertyChanged(nameof(RangeHighHz));
    }
    public string? SelectedChannel
    {
        get => selectedChannel;
        set { if (SetProperty(ref selectedChannel, value)) UpdateCurve(); }
    }
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string WindowText { get => windowText; private set => SetProperty(ref windowText, value); }
    public string ProvenanceText { get => provenanceText; private set => SetProperty(ref provenanceText, value); }

    public void SetPendingWindow(double targetSeconds) =>
        StatusText = $"正在读取 {targetSeconds:0.###} s 目标；当前仍显示 {WindowText}。";

    public void InitializeChannels(IReadOnlyList<string> value)
    {
        if (!channels.SequenceEqual(value, StringComparer.Ordinal))
        {
            channels = value.ToArray();
            Rows.Clear();
            foreach (var channel in channels) Rows.Add(new ReviewCustomFrequencyRow(channel));
            SelectedChannel = channels.FirstOrDefault();
        }
        Clear("等待当前 PSD/RBP 窗口。");
    }

    public void Clear(string reason)
    {
        bins = null;
        PsdCurve = [];
        WindowText = "分析范围不可用";
        ProvenanceText = "";
        StatusText = reason;
        RaisePropertyChanged(nameof(HasBins));
        RaiseFrequencyDomainChanged();
        RaisePropertyChanged(nameof(FrequenciesHz));
        RaisePropertyChanged(nameof(BinIntervals));
        RaisePropertyChanged(nameof(ActualFrequencyHz));
        RaisePropertyChanged(nameof(SelectedBinIndex));
        ClearRows(reason);
    }

    public void ApplyBins(WindowPreviewResponse psd, WindowPreviewResponse rbp, CustomFrequencyBinsResponse value)
    {
        var started = Stopwatch.GetTimestamp();
        ValidatePair(psd, rbp, value.RecordingId, value.StartSample, value.EndSample,
            value.PsdProvenance, value.RbpProvenance, value.Channels.Select(item => item.Channel));
        if (value.Mode != "bins" || value.FrequencyHz.Count < 2 ||
            value.FrequencyHz.Any(frequency => !double.IsFinite(frequency) || frequency < 1 || frequency > 50) ||
            value.FrequencyHz.Zip(value.FrequencyHz.Skip(1)).Any(pair => pair.First >= pair.Second) ||
            value.BinIntervals.Count != value.FrequencyHz.Count ||
            value.BinIntervals.Where((interval, index) =>
                !double.IsFinite(interval.LowHz) || !double.IsFinite(interval.HighHz) ||
                interval.LowHz > value.FrequencyHz[index] || interval.HighHz < value.FrequencyHz[index] ||
                interval.LowHz < 1 || interval.HighHz > 50 || interval.LowHz >= interval.HighHz).Any() ||
            value.Channels.Any(row => row.PsdDensityUv2PerHz.Count != value.FrequencyHz.Count ||
                row.RbpAbsolutePowerUv2.Count != value.FrequencyHz.Count ||
                row.RbpRelativePowerPercent.Count != value.FrequencyHz.Count ||
                row.AmplitudeRmsUv.Count != value.FrequencyHz.Count ||
                row.PsdDensityUv2PerHz.Concat(row.RbpAbsolutePowerUv2).Concat(row.RbpRelativePowerPercent)
                    .Concat(row.AmplitudeRmsUv).Append(row.RbpDenominatorUv2).Any(InvalidMetric)))
            throw new InvalidOperationException("频率投影的 bin 轴、边界或导联数据维度不一致。");
        var validationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        bins = value;
        SnapRangeToCurrentAxis();
        RaiseFrequencyDomainChanged();
        if (selectedChannel is null || !channels.Contains(selectedChannel, StringComparer.Ordinal))
            SelectedChannel = channels.FirstOrDefault();
        if (IsFrequencyValid) selectedFrequencyHz = value.FrequencyHz[FindNearestBin(selectedFrequencyHz)];
        RaisePropertyChanged(nameof(FrequenciesHz));
        RaisePropertyChanged(nameof(HasBins));
        RaisePropertyChanged(nameof(BinIntervals));
        RaisePropertyChanged(nameof(SelectedFrequencyHz));
        RaisePropertyChanged(nameof(ActualFrequencyHz));
        RaisePropertyChanged(nameof(SelectedBinIndex));
        WindowText = $"{psd.WindowStartSeconds:0.###}–{psd.WindowEndSeconds:0.###} s · 样本 [{value.StartSample}, {value.EndSample})";
        ProvenanceText = $"PSD：{value.PsdProvenance.ScientificVersion} / {value.PsdProvenance.WindowId}\n" +
            $"RBP：{value.RbpProvenance.ScientificVersion} / {value.RbpProvenance.WindowId}";
        StatusText = "当前表格投影来自匹配的 PSD/RBP 分析窗口；数值单位由后端响应声明。";
        var curveStarted = Stopwatch.GetTimestamp();
        UpdateCurve();
        var curveMs = Stopwatch.GetElapsedTime(curveStarted).TotalMilliseconds;
        var rowsStarted = Stopwatch.GetTimestamp();
        if (Mode == ReviewCustomFrequencyMode.SingleFrequency) UpdatePointRows();
        else ClearRows("正在等待自定义范围投影。");
        if (DesktopLog.PerformanceDiagnosticsEnabled)
            DesktopLog.Info($"review_custom_frequency_model_perf psd_window_id={PsdWindowId} rbp_window_id={RbpWindowId}" +
                $" stage=bins rows={Rows.Count} bins={value.FrequencyHz.Count} validation_ms={validationMs:0.000}" +
                $" curve_notify_ms={curveMs:0.000} rows_notify_ms={Stopwatch.GetElapsedTime(rowsStarted).TotalMilliseconds:0.000}" +
                $" total_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.000}");
    }

    public void ApplyRange(CustomFrequencyRangeResponse value)
    {
        var started = Stopwatch.GetTimestamp();
        if (Mode != ReviewCustomFrequencyMode.Range || bins is null || value.Mode != "range" || value.RecordingId != bins.RecordingId ||
            value.StartSample != bins.StartSample || value.EndSample != bins.EndSample ||
            value.PsdProvenance != bins.PsdProvenance || value.RbpProvenance != bins.RbpProvenance ||
            !value.Channels.Select(item => item.Channel).SequenceEqual(channels, StringComparer.Ordinal) ||
            value.Channels.Any(row => new[] { row.PsdDensityUv2PerHz, row.RbpAbsolutePowerUv2,
                row.RbpRelativePowerPercent, row.AmplitudeRmsUv, row.RbpDenominatorUv2 }.Any(InvalidMetric)))
            throw new InvalidOperationException("范围投影与当前 PSD/RBP 窗口或导联顺序不一致。");
        if (value.LowHz != RangeLowHz || value.HighHz != RangeHighHz)
            throw new InvalidOperationException("范围投影边界已过期。");
        var validationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var rowsStarted = Stopwatch.GetTimestamp();
        for (var i = 0; i < channels.Count; i++) Rows[i].Update(value.Channels[i]);
        StatusText = $"范围 {value.LowHz:0.###}–{value.HighHz:0.###} Hz；RBP 相对功率分母为完整 1–50 Hz 总功率。";
        if (DesktopLog.PerformanceDiagnosticsEnabled)
            DesktopLog.Info($"review_custom_frequency_model_perf psd_window_id={PsdWindowId} rbp_window_id={RbpWindowId}" +
                $" stage=range rows={Rows.Count} validation_ms={validationMs:0.000}" +
                $" rows_notify_ms={Stopwatch.GetElapsedTime(rowsStarted).TotalMilliseconds:0.000}" +
                $" total_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.000}");
    }

    public void SelectFrequency(double value)
    {
        var selected = SnapToCurrentAxis(value);
        if (!SetProperty(ref selectedFrequencyHz, selected, nameof(SelectedFrequencyHz))) return;
        RaisePropertyChanged(nameof(IsFrequencyValid));
        RaisePropertyChanged(nameof(ActualFrequencyHz));
        RaisePropertyChanged(nameof(SelectedBinIndex));
        if (Mode == ReviewCustomFrequencyMode.SingleFrequency) UpdatePointRows();
    }

    public void ClearRangeProjection(string reason)
    {
        ClearRows(reason);
        StatusText = reason;
    }

    private void UpdatePointRows()
    {
        if (!IsFrequencyValid)
        {
            ClearRangeProjection($"频率需为 {MinimumFrequencyHz:0.###}–{MaximumFrequencyHz:0.###} Hz 内的有限数值。");
            return;
        }
        if (bins is null) return;
        var bin = FindNearestBin(selectedFrequencyHz);
        foreach (var row in Rows)
        {
            var source = bins.Channels.Single(item => item.Channel == row.Channel);
            row.Update(source, bin);
        }
        StatusText = $"当前频率 {bins.FrequencyHz[bin]:0.###} Hz；振幅为所选实际 bin 的等效 RMS。";
    }

    private void UpdateCurve()
    {
        var channel = bins?.Channels.FirstOrDefault(item => item.Channel == selectedChannel);
        PsdCurve = channel is null ? [] : bins!.FrequencyHz.Select((frequency, index) =>
            new ReviewCustomFrequencyCurvePoint(frequency, channel.PsdDensityUv2PerHz[index])).ToArray();
    }

    private int FindNearestBin(double value)
    {
        var axis = bins?.FrequencyHz ?? [];
        if (axis.Count == 0) return 0;
        var low = 0;
        var high = axis.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (axis[middle] == value) return middle;
            if (axis[middle] < value) low = middle + 1;
            else high = middle - 1;
        }
        if (low >= axis.Count) return axis.Count - 1;
        if (high < 0) return 0;
        return value - axis[high] <= axis[low] - value ? high : low;
    }

    private double SnapToCurrentAxis(double value) =>
        bins is not null && double.IsFinite(value) &&
        value >= MinimumFrequencyHz && value <= MaximumFrequencyHz
            ? bins.FrequencyHz[FindNearestBin(value)]
            : value;

    private void SnapRangeToCurrentAxis()
    {
        var low = SnapToCurrentAxis(rangeLowHz);
        var high = SnapToCurrentAxis(rangeHighHz);
        var lowChanged = low != rangeLowHz;
        var highChanged = high != rangeHighHz;
        rangeLowHz = low;
        rangeHighHz = high;
        if (lowChanged) RaisePropertyChanged(nameof(RangeLowHz));
        if (highChanged) RaisePropertyChanged(nameof(RangeHighHz));
    }

    private void ClearRows(string reason)
    {
        foreach (var row in Rows) row.Clear(reason);
    }

    private void OnRangeChanged()
    {
        if (Mode == ReviewCustomFrequencyMode.Range) ClearRangeProjection("正在等待自定义范围投影。");
        RaisePropertyChanged(nameof(IsRangeValid));
    }

    private void RaiseFrequencyDomainChanged()
    {
        RaisePropertyChanged(nameof(MinimumFrequencyHz));
        RaisePropertyChanged(nameof(MaximumFrequencyHz));
        RaisePropertyChanged(nameof(IsFrequencyValid));
        RaisePropertyChanged(nameof(IsRangeValid));
    }

    private static bool InvalidMetric(double? value) => value is { } number && (!double.IsFinite(number) || number < 0);

    private void ValidatePair(WindowPreviewResponse psd, WindowPreviewResponse rbp, string recordingId,
        long startSample, long endSample, CustomFrequencyProvenance psdProvenance,
        CustomFrequencyProvenance rbpProvenance, IEnumerable<string> rowChannels)
    {
        if (psd.RecordingId != recordingId || rbp.RecordingId != recordingId ||
            psd.Window is not { } psdRange || rbp.Window is not { } rbpRange ||
            psdRange.StartSample != startSample || psdRange.EndSample != endSample ||
            rbpRange.StartSample != startSample || rbpRange.EndSample != endSample ||
            psdRange.WindowId != psdProvenance.WindowId || rbpRange.WindowId != rbpProvenance.WindowId ||
            !MatchesIdentity(psdProvenance, psd) || !MatchesIdentity(rbpProvenance, rbp) ||
            !rowChannels.SequenceEqual(channels, StringComparer.Ordinal) ||
            !channels.SequenceEqual(Rows.Select(row => row.Channel), StringComparer.Ordinal))
            throw new InvalidOperationException("频率投影与当前 PSD/RBP 窗口或通道顺序不一致。");
    }

    private static bool MatchesIdentity(CustomFrequencyProvenance provenance, WindowPreviewResponse window)
    {
        if (window.BackendIdentity is not { ValueKind: JsonValueKind.Object } identity) return false;
        return new[]
        {
            ("scientific_version", provenance.ScientificVersion),
            ("implementation_identity", provenance.ImplementationIdentity),
            ("configuration_sha256", provenance.ConfigurationSha256),
            ("window_policy_version", provenance.WindowPolicyVersion),
        }.All(item => identity.TryGetProperty(item.Item1, out var field) &&
            field.ValueKind == JsonValueKind.String && field.GetString() == item.Item2);
    }
}

public sealed class ReviewCustomFrequencyRow(string channel) : ObservableObject
{
    private double? amplitudeRmsUv;
    private double? absolutePowerUv2;
    private double? relativePowerPercent;
    private double? psdDensityUv2PerHz;
    private double? rbpDenominatorUv2;
    private string psdQuality = "unknown";
    private string rbpQuality = "unknown";
    private string details = "";

    public string Channel { get; } = channel;
    public double? AmplitudeRmsUv { get => amplitudeRmsUv; private set => SetProperty(ref amplitudeRmsUv, value); }
    public double? AbsolutePowerUv2 { get => absolutePowerUv2; private set => SetProperty(ref absolutePowerUv2, value); }
    public double? RelativePowerPercent { get => relativePowerPercent; private set => SetProperty(ref relativePowerPercent, value); }
    public double? PsdDensityUv2PerHz { get => psdDensityUv2PerHz; private set => SetProperty(ref psdDensityUv2PerHz, value); }
    public double? RbpDenominatorUv2 { get => rbpDenominatorUv2; private set => SetProperty(ref rbpDenominatorUv2, value); }
    public string PsdQuality { get => psdQuality; private set => SetProperty(ref psdQuality, value); }
    public string RbpQuality { get => rbpQuality; private set => SetProperty(ref rbpQuality, value); }
    public string Details { get => details; private set => SetProperty(ref details, value); }

    internal void Update(CustomFrequencyBinChannel row, int bin) => Set(
        row.AmplitudeRmsUv[bin], row.RbpAbsolutePowerUv2[bin], row.RbpRelativePowerPercent[bin],
        row.PsdDensityUv2PerHz[bin], row.RbpDenominatorUv2, row.PsdQuality, row.PsdQualityReasons,
        row.RbpQuality, row.RbpQualityReasons);

    internal void Update(CustomFrequencyRangeChannel row) => Set(
        row.AmplitudeRmsUv, row.RbpAbsolutePowerUv2, row.RbpRelativePowerPercent,
        row.PsdDensityUv2PerHz, row.RbpDenominatorUv2, row.PsdQuality, row.PsdQualityReasons,
        row.RbpQuality, row.RbpQualityReasons);

    internal void Clear(string reason)
    {
        AmplitudeRmsUv = AbsolutePowerUv2 = RelativePowerPercent = PsdDensityUv2PerHz = RbpDenominatorUv2 = null;
        PsdQuality = RbpQuality = "unknown";
        Details = reason;
    }

    private void Set(double? amplitude, double? absolute, double? relative, double? density,
        double? denominator, string psdStatus, IReadOnlyList<string> psdReasons,
        string rbpStatus, IReadOnlyList<string> rbpReasons)
    {
        AmplitudeRmsUv = amplitude;
        AbsolutePowerUv2 = absolute;
        RelativePowerPercent = relative;
        PsdDensityUv2PerHz = density;
        RbpDenominatorUv2 = denominator;
        PsdQuality = psdStatus;
        RbpQuality = rbpStatus;
        Details = $"PSD：{Describe(psdStatus, psdReasons)}\nRBP：{Describe(rbpStatus, rbpReasons)}\n" +
            $"振幅 RMS：{Format(amplitude)} µV\n绝对功率：{Format(absolute)} µV²\n" +
            $"相对功率：{Format(relative)} %\nPSD：{Format(density)} µV²/Hz\n" +
            $"RBP 1–50 Hz 总功率：{Format(denominator)} µV²";
    }

    private static string Describe(string status, IReadOnlyList<string> reasons) =>
        reasons.Count == 0 ? status : $"{status}；{string.Join("、", reasons)}";

    private static string Format(double? value) => value?.ToString("G6", CultureInfo.CurrentUICulture) ?? "不可用";
}
