using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.BandRatio;

public sealed class BandRatioDetailViewModel : AlgorithmSpecificDetailViewModel, IAlgorithmQualitySource
{
    private BandRatioResultPreview? preview;
    private bool failureDetailsExpanded;

    internal BandRatioDetailViewModel(AlgorithmListViewModel catalog) : base(catalog, "band_ratio") => Refresh();

    public BandRatioResultPoint? CurrentPoint => preview?.CurrentAt(Catalog.DynamicPreviewCursorSeconds);
    public IReadOnlyList<BandRatioResultPoint> TrendPoints => preview?.VisibleThrough(Catalog.DynamicPreviewCursorSeconds) ?? [];
    public bool IsDynamicResult => preview?.IsDynamic == true;
    public double TimelineStartSeconds => preview?.DynamicPoints.FirstOrDefault()?.StartSeconds ?? 0;
    public double TimelineEndSeconds => preview?.DynamicPoints.LastOrDefault()?.EndSeconds ?? 1;
    public string ValueText => CurrentPoint?.Value is double value ? value.ToString("0.###") : "不可用";
    public string ChannelText => preview?.Channel is { Length: > 0 } channel ? channel : "未提供";
    public string WindowText => CurrentPoint is { } point && double.IsFinite(point.StartSeconds) && double.IsFinite(point.EndSeconds) ? $"{point.StartSeconds:0.###}–{point.EndSeconds:0.###} s" : "尚未到达分析窗口";
    public string NumeratorRangeText => FormatRange(CurrentPoint?.Evidence.NumeratorLowHz, CurrentPoint?.Evidence.NumeratorHighHz);
    public string DenominatorRangeText => FormatRange(CurrentPoint?.Evidence.DenominatorLowHz, CurrentPoint?.Evidence.DenominatorHighHz);
    public string NumeratorPowerText => FormatPower(CurrentPoint?.Evidence.NumeratorPowerV2);
    public string DenominatorPowerText => FormatPower(CurrentPoint?.Evidence.DenominatorPowerV2);
    public string FormulaText => CurrentPoint?.Evidence.Formula is { Length: > 0 } formula ? formula : "分子频段功率 ÷ 分母频段功率";
    public string QualityText => CurrentPoint is null ? (Catalog.LastRun is null ? "尚未运行分析" : "尚未到达分析窗口") : CurrentPoint.Value is null ? "当前窗口不可用" : AlgorithmResultFormatter.FormatQualityStatus(CurrentPoint.Quality);
    public int UnavailableWindowCount => TrendPoints.Count(point => point.State == "Unavailable");
    public int RejectedWindowCount => TrendPoints.Count(point => point.State == "Rejected" || point.Quality == "gate_failed");
    public string WindowStateSummary => preview is not { IsDynamic: true } ? "静态分析" : TrendPoints.Count == 0 ? "暂无窗口状态" : string.Join("、", TrendPoints.GroupBy(point => point.State).Select(group => $"{AlgorithmResultFormatter.FormatWindowStateForDisplay(group.Key)}：{group.Count()}"));
    public string FailureReasonText => Catalog.LastRun?.Error?.Message ?? (TrendPoints.LastOrDefault(point => point.Failure.Length > 0)?.Failure ?? "暂无失败原因");
    public string FailureDetailsText => Catalog.LastRun?.Error?.Message ?? (TrendPoints.Any(point => point.Failure.Length > 0) ? string.Join(Environment.NewLine, TrendPoints.Where(point => point.Failure.Length > 0).Select(point => $"{point.StartSeconds:0.###}–{point.EndSeconds:0.###} s：{point.Failure}")) : "暂无失败窗口。");
    public bool IsFailureDetailsExpanded { get => failureDetailsExpanded; private set => SetProperty(ref failureDetailsExpanded, value); }
    public ICommand ToggleFailureDetailsCommand => new RelayCommand(() => IsFailureDetailsExpanded = !IsFailureDetailsExpanded);

    protected override void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        base.OnCatalogPropertyChanged(sender, args);
        if (args.PropertyName is nameof(AlgorithmListViewModel.LastRun) or nameof(AlgorithmListViewModel.SelectedAlgorithm)) Refresh();
        else if (args.PropertyName == nameof(AlgorithmListViewModel.DynamicPreviewCursorSeconds)) RaiseResults();
    }

    private void Refresh()
    {
        preview = Algorithm?.Id == "band_ratio" ? BandRatioResultPreview.Parse(Catalog.LastRun?.ResultSummary) : null;
        IsFailureDetailsExpanded = false;
        RaisePropertyChanged(nameof(IsDynamicResult)); RaisePropertyChanged(nameof(TimelineStartSeconds)); RaisePropertyChanged(nameof(TimelineEndSeconds)); RaisePropertyChanged(nameof(ChannelText)); RaiseResults();
    }

    private void RaiseResults()
    {
        foreach (var name in new[] { nameof(CurrentPoint), nameof(TrendPoints), nameof(ValueText), nameof(WindowText), nameof(NumeratorRangeText), nameof(DenominatorRangeText), nameof(NumeratorPowerText), nameof(DenominatorPowerText), nameof(FormulaText), nameof(QualityText), nameof(UnavailableWindowCount), nameof(RejectedWindowCount), nameof(WindowStateSummary), nameof(FailureReasonText), nameof(FailureDetailsText) }) RaisePropertyChanged(name);
    }

    private static string FormatRange(double? low, double? high) => low is double lo && high is double hi ? $"{lo:0.##}–{hi:0.##} Hz" : "未提供";
    private static string FormatPower(double? value) => value is double number && double.IsFinite(number)
        ? $"{number * 1e12:0.###} μV²"
        : "不可用";
}
