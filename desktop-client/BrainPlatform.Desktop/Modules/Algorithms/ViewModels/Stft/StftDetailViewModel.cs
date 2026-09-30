using System.Text.Json;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;

public enum StftChartKind { Heatmap, Spectrum, FrequencyTrend }

public sealed record StftChartOption(StftChartKind Kind, string Label);

public sealed class StftDetailViewModel : AlgorithmSpecificDetailViewModel
{
    private bool isFailureDetailsExpanded;
    private StftChartOption selectedChart;
    private double selectedTimeIndex;
    private double selectedFrequencyIndex;
    internal StftDetailViewModel(AlgorithmListViewModel catalog) : base(catalog, "stft") => selectedChart = ChartOptions[0];

    public StftPreview? Preview => Catalog.StftResult;
    public IReadOnlyList<StftChartOption> ChartOptions { get; } =
    [
        new(StftChartKind.Heatmap, "时频热图"),
        new(StftChartKind.Spectrum, "频谱切片"),
        new(StftChartKind.FrequencyTrend, "单频功率趋势"),
    ];
    public StftChartOption SelectedChart
    {
        get => selectedChart;
        set
        {
            if (value is null || !ChartOptions.Contains(value) || !SetProperty(ref selectedChart, value)) return;
            RaisePropertyChanged(nameof(IsHeatmapSelected));
            RaisePropertyChanged(nameof(IsSpectrumSelected));
            RaisePropertyChanged(nameof(IsTrendSelected));
        }
    }
    public bool IsHeatmapSelected => SelectedChart.Kind == StftChartKind.Heatmap;
    public bool IsSpectrumSelected => SelectedChart.Kind == StftChartKind.Spectrum;
    public bool IsTrendSelected => SelectedChart.Kind == StftChartKind.FrequencyTrend;
    public double MaximumTimeIndex => Math.Max(0, (Preview?.TimesSeconds.Length ?? 1) - 1);
    public double MaximumFrequencyIndex => Math.Max(0, (Preview?.FrequenciesHz.Length ?? 1) - 1);
    public double SelectedTimeIndex
    {
        get => selectedTimeIndex;
        set
        {
            if (!SetProperty(ref selectedTimeIndex, Math.Clamp(Math.Round(value), 0, MaximumTimeIndex))) return;
            RaisePropertyChanged(nameof(SelectedTimeText));
        }
    }
    public double SelectedFrequencyIndex
    {
        get => selectedFrequencyIndex;
        set
        {
            if (!SetProperty(ref selectedFrequencyIndex, Math.Clamp(Math.Round(value), 0, MaximumFrequencyIndex))) return;
            RaisePropertyChanged(nameof(SelectedFrequencyText));
        }
    }
    public string SelectedTimeText => Preview is { } preview
        ? $"时频中心：{preview.TimesSeconds[(int)SelectedTimeIndex]:0.###} s" : "时频中心：--";
    public string SelectedFrequencyText => Preview is { } preview
        ? $"频率：{preview.FrequenciesHz[(int)SelectedFrequencyIndex]:0.###} Hz" : "频率：--";
    public bool HasPreview => Preview is not null;
    public string StatusText => Preview is not null
        ? Catalog.IsDynamicMode ? Catalog.DynamicCurrentWindowText : "静态时频结果"
        : Catalog.LastRun?.Error is { } error ? $"结果不可用：{AlgorithmResultFormatter.FormatFailureCodeForDisplay(error.Code)}：{(string.IsNullOrWhiteSpace(error.Message) ? "后端未返回具体原因，请检查后端日志" : error.Message)}"
        : Catalog.StructuredPreviewText.StartsWith("结构化预览不可用", StringComparison.Ordinal) ||
          Catalog.StructuredPreviewText.StartsWith("STFT 结果已保存", StringComparison.Ordinal)
            ? Catalog.StructuredPreviewText
        : Catalog.IsDynamicMode && Catalog.DynamicPreviewRows.LastOrDefault() is { } row
            ? $"当前窗口：{row.State}。{row.Failure}"
            : "暂无可显示的时频结果";
    public string FailureReasonText => Catalog.LastRun?.Error is { } error
        ? $"{AlgorithmResultFormatter.FormatFailureCodeForDisplay(error.Code)}：{(string.IsNullOrWhiteSpace(error.Message) ? "后端未返回具体原因，请检查后端日志" : error.Message)}"
        : QualityFailureReason(Catalog.StftStructuredPreview?.Quality) ??
          (Catalog.DynamicPreviewRows.FirstOrDefault(row => !string.IsNullOrWhiteSpace(row.Failure)) is { } row
              ? row.Failure
              : "暂无失败原因");
    public bool HasFailureDetails => Catalog.LastRun?.Error is not null ||
        HasQualityFailure(Catalog.StftStructuredPreview?.Quality) ||
        Catalog.DynamicPreviewRows.Any(row => !string.IsNullOrWhiteSpace(row.Failure));
    public string FailureDetailsText => Catalog.LastRun?.Error is { } error
        ? BuildFailureDetails(error)
        : BuildQualityFailureDetails(Catalog.StftStructuredPreview?.Quality) ??
          string.Join(Environment.NewLine, Catalog.DynamicPreviewRows
              .Where(row => !string.IsNullOrWhiteSpace(row.Failure))
              .Select(row => $"{row.StartSeconds:0.###}–{row.EndSeconds:0.###} s：{row.Failure}"));
    public int UnavailableWindowCount => Catalog.StftStructuredPreview?.WindowStateCounts.TryGetValue("Unavailable", out var unavailable) == true ? unavailable : 0;
    public int RejectedWindowCount => Catalog.StftStructuredPreview?.WindowStateCounts.TryGetValue("Rejected", out var rejected) == true ? rejected : 0;
    public string WindowStateSummary => Catalog.StftStructuredPreview is null ? "未产生窗口结果" : WindowStateText;
    public bool IsFailureDetailsExpanded
    {
        get => isFailureDetailsExpanded;
        private set => SetProperty(ref isFailureDetailsExpanded, value);
    }
    public ICommand ToggleFailureDetailsCommand => new RelayCommand(() => IsFailureDetailsExpanded = !IsFailureDetailsExpanded);
    public string TimeRangeText => Preview is { } preview
        ? $"时频中心：{preview.TimesSeconds[0]:0.###}–{preview.TimesSeconds[^1]:0.###} s"
        : "--";
    public string RequestedRangeText => Catalog.LastRun?.RequestedRange is { } range
        ? $"请求范围：{range.StartSeconds:0.###}–{range.EndSeconds:0.###} s"
        : "请求范围：--";
    public string FrequencyRangeText => Preview is { } preview
        ? $"频率：{preview.FrequenciesHz[0]:0.###}–{preview.FrequenciesHz[^1]:0.###} Hz"
        : "--";
    public string QualityText => Catalog.StftStructuredPreview?.Quality is { } quality &&
        quality.ValueKind == System.Text.Json.JsonValueKind.Object && quality.TryGetProperty("status", out var status)
        ? AlgorithmResultFormatter.FormatQualityForDisplay(quality)
        : Catalog.LastRun?.Error?.Message ?? "尚未生成结果";
    public string WindowStateText => Catalog.StftStructuredPreview is { } preview && preview.WindowStateCounts.Count > 0
        ? string.Join("、", preview.WindowStateCounts.Select(item => $"{AlgorithmResultFormatter.FormatWindowStateForDisplay(item.Key)}：{item.Value}"))
        : "无动态窗口";
    public string RunIdText => Catalog.LastRun?.RunId ?? "未运行";
    public string ActualRangeText => Catalog.LastRun?.ActualRange is { } range
        ? $"{range.StartSeconds:0.###}–{range.EndSeconds:0.###} s" : "--";
    public string ScientificVersionText => Catalog.LastRun?.ScientificVersion ?? "--";
    public IReadOnlyList<DynamicWindowRow> DynamicWindowRows => Catalog.DynamicWindowRows;

    protected override void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        base.OnCatalogPropertyChanged(sender, args);
        if (args.PropertyName is nameof(AlgorithmListViewModel.StftResult)
            or nameof(AlgorithmListViewModel.SelectedAnalysisMode)
            or nameof(AlgorithmListViewModel.DynamicPreviewCursorSeconds)
            or nameof(AlgorithmListViewModel.DynamicPreviewRows)
            or nameof(AlgorithmListViewModel.StftStructuredPreview)
            or nameof(AlgorithmListViewModel.StructuredPreviewText)
            or nameof(AlgorithmListViewModel.LastRun))
        {
            if (args.PropertyName == nameof(AlgorithmListViewModel.StftResult))
            {
                SelectedTimeIndex = Math.Min(SelectedTimeIndex, MaximumTimeIndex);
                SelectedFrequencyIndex = Math.Min(SelectedFrequencyIndex, MaximumFrequencyIndex);
                RaisePropertyChanged(nameof(MaximumTimeIndex));
                RaisePropertyChanged(nameof(MaximumFrequencyIndex));
                RaisePropertyChanged(nameof(SelectedTimeText));
                RaisePropertyChanged(nameof(SelectedFrequencyText));
            }
            RaisePropertyChanged(nameof(Preview));
            RaisePropertyChanged(nameof(HasPreview));
            RaisePropertyChanged(nameof(StatusText));
            RaisePropertyChanged(nameof(FailureReasonText));
            RaisePropertyChanged(nameof(HasFailureDetails));
            RaisePropertyChanged(nameof(FailureDetailsText));
            RaisePropertyChanged(nameof(UnavailableWindowCount));
            RaisePropertyChanged(nameof(RejectedWindowCount));
            RaisePropertyChanged(nameof(WindowStateSummary));
            RaisePropertyChanged(nameof(TimeRangeText));
            RaisePropertyChanged(nameof(RequestedRangeText));
            RaisePropertyChanged(nameof(FrequencyRangeText));
            RaisePropertyChanged(nameof(QualityText));
            RaisePropertyChanged(nameof(WindowStateText));
            RaisePropertyChanged(nameof(RunIdText));
            RaisePropertyChanged(nameof(ActualRangeText));
            RaisePropertyChanged(nameof(ScientificVersionText));
            RaisePropertyChanged(nameof(DynamicWindowRows));
        }
    }

    private static string BuildFailureDetails(StructuredRunError error)
    {
        var lines = new List<string>
        {
            $"错误代码：{AlgorithmResultFormatter.FormatFailureCodeForDisplay(error.Code)}（{error.Code}）",
            $"错误信息：{(string.IsNullOrWhiteSpace(error.Message) ? "后端未返回具体原因，请检查后端日志" : error.Message)}",
        };
        if (!string.IsNullOrWhiteSpace(error.Stage)) lines.Add($"失败阶段：{error.Stage}");
        if (error.Details is { } details && details.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var property in details.EnumerateObject())
                lines.Add($"• {property.Name}：{property.Value}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static bool HasQualityFailure(JsonElement? quality) =>
        quality is { ValueKind: JsonValueKind.Object } &&
        (quality.Value.TryGetProperty("gate_failed", out var gate) && gate.ValueKind == JsonValueKind.String ||
         quality.Value.TryGetProperty("bad_windows", out var bad) && bad.TryGetInt32(out var count) && count > 0 ||
         quality.Value.TryGetProperty("bad_segments", out var badSegments) && badSegments.TryGetInt32(out var segments) && segments > 0);

    private static string? QualityFailureReason(JsonElement? quality)
    {
        if (quality is not { ValueKind: JsonValueKind.Object } value || !HasQualityFailure(value)) return null;
        if (value.TryGetProperty("gate_failed", out var gate) && gate.ValueKind == JsonValueKind.String)
            return $"质量门未通过：{AlgorithmResultFormatter.FormatQualityReasonForDisplay(gate.GetString() ?? "unknown")}";
        if (value.TryGetProperty("rejected_reasons", out var reasons) && reasons.ValueKind == JsonValueKind.Array)
            return $"质量门未通过：{string.Join("、", reasons.EnumerateArray().Select(item => AlgorithmResultFormatter.FormatQualityReasonForDisplay(item.GetString() ?? "unknown")))}";
        return "质量门未通过：存在被拒绝窗口";
    }

    private static string? BuildQualityFailureDetails(JsonElement? quality)
    {
        if (quality is not { ValueKind: JsonValueKind.Object } value || !HasQualityFailure(value)) return null;
        var lines = new List<string>();
        AddNumber(value, lines, "clean_windows", "有效窗口数");
        AddNumber(value, lines, "total_windows", "候选窗口数");
        AddNumber(value, lines, "bad_windows", "被拒绝窗口数");
        if (value.TryGetProperty("clean_ratio", out var ratio) && ratio.TryGetDouble(out var cleanRatio))
            lines.Add($"有效比例：{cleanRatio:P1}");
        var reason = QualityFailureReason(value);
        if (reason is not null) lines.Add(reason);
        if (value.TryGetProperty("rejected_reasons", out var reasons) && reasons.ValueKind == JsonValueKind.Array)
            lines.Add($"拒绝原因：{string.Join("、", reasons.EnumerateArray().Select(item => AlgorithmResultFormatter.FormatQualityReasonForDisplay(item.GetString() ?? "unknown")))}");
        return string.Join(Environment.NewLine, lines);
    }

    private static void AddNumber(JsonElement value, ICollection<string> lines, string key, string label)
    {
        if (value.TryGetProperty(key, out var item) && item.TryGetDouble(out var number))
            lines.Add($"{label}：{number:0.###}");
    }
}
