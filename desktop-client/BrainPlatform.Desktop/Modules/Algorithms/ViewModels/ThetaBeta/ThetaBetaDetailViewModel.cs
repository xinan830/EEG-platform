using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.ThetaBeta;

public sealed class ThetaBetaDetailViewModel : AlgorithmSpecificDetailViewModel, IAlgorithmQualitySource
{
    private ThetaBetaResultPreview? preview;
    private bool isFailureDetailsExpanded;
    internal ThetaBetaDetailViewModel(AlgorithmListViewModel catalog) : base(catalog, "theta_beta") => RefreshPreview();
    public ThetaBetaResultPoint? CurrentPoint => preview?.CurrentAt(Catalog.DynamicPreviewCursorSeconds);
    public IReadOnlyList<ThetaBetaResultPoint> TrendPoints => preview?.VisibleThrough(Catalog.DynamicPreviewCursorSeconds) ?? [];
    public bool IsDynamicResult => preview?.IsDynamic == true;
    public double TimelineStartSeconds => preview?.DynamicPoints.FirstOrDefault()?.StartSeconds ?? 0;
    public double TimelineEndSeconds => preview?.DynamicPoints.LastOrDefault()?.EndSeconds ?? 1;
    public string ValueText => CurrentPoint?.Value is double value ? value.ToString("0.###") : "不可用";
    public string ChannelText => preview?.Channel is { Length: > 0 } channel ? channel : "未提供";
    public string WindowText => CurrentPoint is { } point && double.IsFinite(point.StartSeconds) && double.IsFinite(point.EndSeconds) ? $"{point.StartSeconds:0.###}–{point.EndSeconds:0.###} s" : "尚未到达分析窗口";
    public string IapfText => FormatHz(CurrentPoint?.Evidence.IapfHz);
    public string ThetaRangeText => FormatRange(CurrentPoint?.Evidence.ThetaLowHz, CurrentPoint?.Evidence.ThetaHighHz);
    public string BetaRangeText => FormatRange(CurrentPoint?.Evidence.BetaLowHz, CurrentPoint?.Evidence.BetaHighHz);
    public string ThetaPowerText => FormatPower(CurrentPoint?.Evidence.ThetaPowerUv2);
    public string BetaPowerText => FormatPower(CurrentPoint?.Evidence.BetaPowerUv2);
    public string FormulaText => CurrentPoint?.Evidence.Formula is { Length: > 0 } formula ? formula : "Theta 功率 ÷ Beta 功率";
    public string StatusText => CurrentPoint is not { } point ? Catalog.LastRun?.Error?.Message ?? "尚无结果" : point.Value is null ? point.Failure is { Length: > 0 } failure ? failure : point.State == "Partial" ? "尚未形成完整窗口" : "当前窗口不可用" : point.Quality == "clean" ? "质量门通过" : AlgorithmResultFormatter.FormatQualityStatus(point.Quality);
    public string QualityText => Catalog.LastRun?.Error is not null ? "运行失败" : CurrentPoint is not { } current ? Catalog.LastRun is null ? "尚未运行分析" : "尚未到达分析窗口" : current.State == "Partial" ? "窗口未完整" : AlgorithmResultFormatter.FormatQualityStatus(current.Quality);
    private IReadOnlyList<ThetaBetaResultPoint> ReleasedPoints => preview?.VisibleThrough(Catalog.DynamicPreviewCursorSeconds) ?? [];
    public int UnavailableWindowCount => ReleasedPoints.Count(point => point.State == "Unavailable");
    public int RejectedWindowCount => ReleasedPoints.Count(point => point.State == "Rejected" || point.Quality == "gate_failed");
    public string WindowStateSummary => preview is null ? "暂无窗口状态" : !preview.IsDynamic ? "静态分析" : ReleasedPoints.Count == 0 ? "暂无窗口状态" : string.Join("、", ReleasedPoints.GroupBy(point => point.State).Select(group => $"{AlgorithmResultFormatter.FormatWindowStateForDisplay(group.Key)}：{group.Count()}"));
    private IEnumerable<ThetaBetaResultPoint> ReleasedFailures => ReleasedPoints.Where(point => point.Failure.Length > 0);
    public string FailureReasonText => Catalog.LastRun?.Error?.Message ?? (preview is not { IsDynamic: true } ? CurrentPoint?.Failure is { Length: > 0 } failure ? failure : "暂无失败原因" : ReleasedFailures.LastOrDefault() is { } latest ? $"已有 {ReleasedFailures.Count()} 个失败窗口；最近原因：{latest.Failure}" : "暂无失败原因");
    public string FailureDetailsText => Catalog.LastRun?.Error?.Message ?? (preview is not { IsDynamic: true } ? CurrentPoint?.Failure is { Length: > 0 } failure ? failure : "暂无失败窗口。" : ReleasedFailures.Any() ? string.Join(Environment.NewLine, ReleasedFailures.Select(point => $"{point.StartSeconds:0.###}–{point.EndSeconds:0.###} s：{point.Failure}")) : "暂无失败窗口。");
    public bool IsFailureDetailsExpanded { get => isFailureDetailsExpanded; private set => SetProperty(ref isFailureDetailsExpanded, value); }
    public ICommand ToggleFailureDetailsCommand => new RelayCommand(() => IsFailureDetailsExpanded = !IsFailureDetailsExpanded);
    protected override void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        base.OnCatalogPropertyChanged(sender, args);
        if (args.PropertyName is nameof(AlgorithmListViewModel.LastRun) or nameof(AlgorithmListViewModel.SelectedAlgorithm)) RefreshPreview();
        else if (args.PropertyName == nameof(AlgorithmListViewModel.DynamicPreviewCursorSeconds)) RaiseResultProperties();
    }
    private void RefreshPreview() { preview = Algorithm?.Id == "theta_beta" ? ThetaBetaResultPreview.Parse(Catalog.LastRun?.ResultSummary) : null; IsFailureDetailsExpanded = false; RaisePropertyChanged(nameof(IsDynamicResult)); RaisePropertyChanged(nameof(TimelineStartSeconds)); RaisePropertyChanged(nameof(TimelineEndSeconds)); RaisePropertyChanged(nameof(ChannelText)); RaiseResultProperties(); }
    private void RaiseResultProperties() { foreach (var name in new[] { nameof(CurrentPoint), nameof(TrendPoints), nameof(ValueText), nameof(WindowText), nameof(IapfText), nameof(ThetaRangeText), nameof(BetaRangeText), nameof(ThetaPowerText), nameof(BetaPowerText), nameof(FormulaText), nameof(StatusText), nameof(QualityText), nameof(UnavailableWindowCount), nameof(RejectedWindowCount), nameof(WindowStateSummary), nameof(FailureReasonText), nameof(FailureDetailsText) }) RaisePropertyChanged(name); }
    private static string FormatHz(double? value) => value is double number ? $"{number:0.##} Hz" : "未提供";
    private static string FormatRange(double? low, double? high) => low is double lo && high is double hi ? $"{lo:0.##}–{hi:0.##} Hz" : "未提供";
    private static string FormatPower(double? value) => value is double number ? $"{number:0.###} μV²" : "未提供";
}
