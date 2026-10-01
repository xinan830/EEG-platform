using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.PeakFrequency;

public sealed class PeakFrequencyDetailViewModel : AlgorithmSpecificDetailViewModel, IAlgorithmQualitySource
{
    private PeakFrequencyResultPreview? preview;
    private bool isFailureDetailsExpanded;

    internal PeakFrequencyDetailViewModel(AlgorithmListViewModel catalog) : base(catalog, "peak_frequency") => RefreshPreview();

    public PeakFrequencyResultPoint? CurrentPoint => preview?.CurrentAt(Catalog.DynamicPreviewCursorSeconds);
    public IReadOnlyList<PeakFrequencyResultPoint> TrendPoints => preview?.VisibleThrough(Catalog.DynamicPreviewCursorSeconds) ?? [];
    public bool IsDynamicResult => preview?.IsDynamic == true;
    public double TimelineStartSeconds => preview?.DynamicPoints.FirstOrDefault()?.StartSeconds ?? 0;
    public double TimelineEndSeconds => preview?.DynamicPoints.LastOrDefault()?.EndSeconds ?? 1;
    public string ValueText => CurrentPoint?.ValueHz is double value ? $"{value:0.##} Hz" : "不可用";
    public string ChannelText => preview?.Channel is { Length: > 0 } channel ? channel : "未提供";
    public string WindowText => CurrentPoint is { } point && double.IsFinite(point.StartSeconds) && double.IsFinite(point.EndSeconds)
        ? $"{point.StartSeconds:0.###}–{point.EndSeconds:0.###} s" : "尚未到达分析窗口";
    public string PeakPowerText => CurrentPoint?.PeakPowerV2PerHz is double power
        ? $"{power * 1e12:0.###} μV²/Hz" : "未提供";
    public string StatusText => CurrentPoint is not { } point
        ? Catalog.LastRun?.Error?.Message ?? "尚无结果"
        : point.ValueHz is null ? point.Failure is { Length: > 0 } failure ? failure : "当前窗口不可用"
        : point.Quality == "clean" ? "质量门通过" : AlgorithmResultFormatter.FormatQualityStatus(point.Quality);
    public string QualityText => Catalog.LastRun?.Error is not null ? "运行失败"
        : CurrentPoint is not { } point ? Catalog.LastRun is null ? "尚未运行分析" : "尚未到达分析窗口"
        : point.State == "Partial" ? "窗口未完整" : AlgorithmResultFormatter.FormatQualityStatus(point.Quality);

    private IReadOnlyList<PeakFrequencyResultPoint> ReleasedPoints => TrendPoints;
    public int UnavailableWindowCount => ReleasedPoints.Count(point => point.State == "Unavailable");
    public int RejectedWindowCount => ReleasedPoints.Count(point => point.State == "Rejected");
    public string WindowStateSummary => preview is null ? "暂无窗口状态"
        : !preview.IsDynamic ? "静态分析"
        : ReleasedPoints.Count == 0 ? "暂无窗口状态"
        : string.Join("、", ReleasedPoints.GroupBy(point => point.State)
            .Select(group => $"{AlgorithmResultFormatter.FormatWindowStateForDisplay(group.Key)}：{group.Count()}"));
    private IEnumerable<PeakFrequencyResultPoint> ReleasedFailures => ReleasedPoints.Where(point => point.Failure.Length > 0);
    public string FailureReasonText => Catalog.LastRun?.Error?.Message ?? (preview is not { IsDynamic: true }
        ? CurrentPoint?.Failure is { Length: > 0 } failure ? failure : "暂无失败原因"
        : ReleasedFailures.LastOrDefault() is { } latest
            ? $"已有 {ReleasedFailures.Count()} 个失败窗口；最近原因：{latest.Failure}"
            : "暂无失败原因");
    public string FailureDetailsText => Catalog.LastRun?.Error?.Message ?? (preview is not { IsDynamic: true }
        ? CurrentPoint?.Failure is { Length: > 0 } failure ? failure : "暂无失败窗口。"
        : ReleasedFailures.Any()
            ? string.Join(Environment.NewLine, ReleasedFailures.Select(point =>
                $"{point.StartSeconds:0.###}–{point.EndSeconds:0.###} s：{point.Failure}"))
            : "暂无失败窗口。");
    public bool IsFailureDetailsExpanded
    {
        get => isFailureDetailsExpanded;
        private set => SetProperty(ref isFailureDetailsExpanded, value);
    }
    public ICommand ToggleFailureDetailsCommand => new RelayCommand(() => IsFailureDetailsExpanded = !IsFailureDetailsExpanded);

    protected override void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        base.OnCatalogPropertyChanged(sender, args);
        if (args.PropertyName is nameof(AlgorithmListViewModel.LastRun) or nameof(AlgorithmListViewModel.SelectedAlgorithm))
            RefreshPreview();
        else if (args.PropertyName is nameof(AlgorithmListViewModel.DynamicPreviewCursorSeconds))
            RaiseResultProperties();
    }

    private void RefreshPreview()
    {
        preview = Algorithm?.Id == "peak_frequency" ? PeakFrequencyResultPreview.Parse(Catalog.LastRun?.ResultSummary) : null;
        IsFailureDetailsExpanded = false;
        RaisePropertyChanged(nameof(IsDynamicResult));
        RaisePropertyChanged(nameof(TimelineStartSeconds));
        RaisePropertyChanged(nameof(TimelineEndSeconds));
        RaisePropertyChanged(nameof(ChannelText));
        RaiseResultProperties();
    }

    private void RaiseResultProperties()
    {
        RaisePropertyChanged(nameof(CurrentPoint));
        RaisePropertyChanged(nameof(TrendPoints));
        RaisePropertyChanged(nameof(ValueText));
        RaisePropertyChanged(nameof(WindowText));
        RaisePropertyChanged(nameof(PeakPowerText));
        RaisePropertyChanged(nameof(StatusText));
        RaisePropertyChanged(nameof(QualityText));
        RaisePropertyChanged(nameof(UnavailableWindowCount));
        RaisePropertyChanged(nameof(RejectedWindowCount));
        RaisePropertyChanged(nameof(WindowStateSummary));
        RaisePropertyChanged(nameof(FailureReasonText));
        RaisePropertyChanged(nameof(FailureDetailsText));
    }
}
