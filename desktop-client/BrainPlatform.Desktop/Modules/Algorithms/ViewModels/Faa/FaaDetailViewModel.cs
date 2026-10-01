using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Faa;

public sealed class FaaDetailViewModel : AlgorithmSpecificDetailViewModel, IAlgorithmQualitySource
{
    private FaaResultPreview? preview; private bool isFailureDetailsExpanded;
    internal FaaDetailViewModel(AlgorithmListViewModel catalog) : base(catalog, "faa") => RefreshPreview();
    public FaaResultPoint? CurrentPoint => preview?.CurrentAt(Catalog.DynamicPreviewCursorSeconds);
    public IReadOnlyList<FaaResultPoint> TrendPoints => preview?.VisibleThrough(Catalog.DynamicPreviewCursorSeconds) ?? [];
    public bool IsDynamicResult => preview?.IsDynamic == true;
    public double TimelineStartSeconds => preview?.DynamicPoints.FirstOrDefault()?.StartSeconds ?? 0;
    public double TimelineEndSeconds => preview?.DynamicPoints.LastOrDefault()?.EndSeconds ?? 1;
    public string ValueText => CurrentPoint?.Value is double value ? value.ToString("0.###") : "不可用";
    public string ChannelText => preview?.Channel is { Length: > 0 } channel ? channel : "未提供";
    public string WindowText => CurrentPoint is { } point && double.IsFinite(point.StartSeconds) && double.IsFinite(point.EndSeconds) ? $"{point.StartSeconds:0.###}–{point.EndSeconds:0.###} s" : "尚未到达分析窗口";
    public string LeftChannelText => CurrentPoint?.Evidence.LeftChannel is { Length: > 0 } value ? value : "未提供";
    public string RightChannelText => CurrentPoint?.Evidence.RightChannel is { Length: > 0 } value ? value : "未提供";
    public string AlphaBandText => CurrentPoint?.Evidence.AlphaBand ?? "8–13 Hz";
    public string LeftPowerText => Power(CurrentPoint?.Evidence.LeftPowerUv2);
    public string RightPowerText => Power(CurrentPoint?.Evidence.RightPowerUv2);
    public string EpochText => CurrentPoint?.Evidence.CleanEpochs is double clean && CurrentPoint.Evidence.TotalEpochs is double total ? $"{clean:0} / {total:0}" : "未提供";
    public string FormulaText => CurrentPoint?.Evidence.Formula is { Length: > 0 } formula ? formula : "ln(F4 Alpha 功率) − ln(F3 Alpha 功率)";
    public string StatusText => CurrentPoint is not { } point ? Catalog.LastRun?.Error?.Message ?? "尚无结果" : point.Value is null ? point.Failure is { Length: > 0 } failure ? failure : "当前窗口不可用" : point.Quality == "clean" ? "质量门通过" : AlgorithmResultFormatter.FormatQualityStatus(point.Quality);
    public string QualityText => Catalog.LastRun?.Error is not null ? "运行失败" : CurrentPoint is not { } point ? Catalog.LastRun is null ? "尚未运行分析" : "尚未到达分析窗口" : AlgorithmResultFormatter.FormatQualityStatus(point.Quality);
    private IReadOnlyList<FaaResultPoint> ReleasedPoints => preview?.VisibleThrough(Catalog.DynamicPreviewCursorSeconds) ?? [];
    public int UnavailableWindowCount => ReleasedPoints.Count(point => point.State == "Unavailable");
    public int RejectedWindowCount => ReleasedPoints.Count(point => point.State == "Rejected" || point.Quality == "gate_failed");
    public string WindowStateSummary => preview is null ? "暂无窗口状态" : !preview.IsDynamic ? "静态分析" : ReleasedPoints.Count == 0 ? "暂无窗口状态" : string.Join("、", ReleasedPoints.GroupBy(point => point.State).Select(group => $"{AlgorithmResultFormatter.FormatWindowStateForDisplay(group.Key)}：{group.Count()}"));
    private IEnumerable<FaaResultPoint> Failures => ReleasedPoints.Where(point => point.Failure.Length > 0);
    public string FailureReasonText => Catalog.LastRun?.Error?.Message ?? (preview is not { IsDynamic: true } ? CurrentPoint?.Failure is { Length: > 0 } failure ? failure : "暂无失败原因" : Failures.LastOrDefault() is { } latest ? $"最近原因：{latest.Failure}" : "暂无失败原因");
    public string FailureDetailsText => Catalog.LastRun?.Error?.Message ?? (Failures.Any() ? string.Join(Environment.NewLine, Failures.Select(point => $"{point.StartSeconds:0.###}–{point.EndSeconds:0.###} s：{point.Failure}")) : "暂无失败窗口。");
    public bool IsFailureDetailsExpanded { get => isFailureDetailsExpanded; private set => SetProperty(ref isFailureDetailsExpanded, value); }
    public ICommand ToggleFailureDetailsCommand => new RelayCommand(() => IsFailureDetailsExpanded = !IsFailureDetailsExpanded);
    protected override void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) { base.OnCatalogPropertyChanged(sender, args); if (args.PropertyName is nameof(AlgorithmListViewModel.LastRun) or nameof(AlgorithmListViewModel.SelectedAlgorithm)) RefreshPreview(); else if (args.PropertyName == nameof(AlgorithmListViewModel.DynamicPreviewCursorSeconds)) RaiseProperties(); }
    private void RefreshPreview() { preview = Algorithm?.Id == "faa" ? FaaResultPreview.Parse(Catalog.LastRun?.ResultSummary) : null; IsFailureDetailsExpanded = false; RaisePropertyChanged(nameof(IsDynamicResult)); RaisePropertyChanged(nameof(TimelineStartSeconds)); RaisePropertyChanged(nameof(TimelineEndSeconds)); RaisePropertyChanged(nameof(ChannelText)); RaiseProperties(); }
    private void RaiseProperties() { foreach (var name in new[] { nameof(CurrentPoint), nameof(TrendPoints), nameof(ValueText), nameof(WindowText), nameof(LeftChannelText), nameof(RightChannelText), nameof(AlphaBandText), nameof(LeftPowerText), nameof(RightPowerText), nameof(EpochText), nameof(FormulaText), nameof(StatusText), nameof(QualityText), nameof(UnavailableWindowCount), nameof(RejectedWindowCount), nameof(WindowStateSummary), nameof(FailureReasonText), nameof(FailureDetailsText) }) RaisePropertyChanged(name); }
    private static string Power(double? value) => value is double number ? $"{number:0.###} μV²" : "未提供";
}
