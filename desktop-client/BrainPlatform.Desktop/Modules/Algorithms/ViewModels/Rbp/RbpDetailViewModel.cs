using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;

public sealed class RbpDetailViewModel : AlgorithmSpecificDetailViewModel, IAlgorithmQualitySource
{
    private RbpResultPreview preview = RbpResultPreview.Parse(null);
    private bool isFailureDetailsExpanded;

    internal RbpDetailViewModel(AlgorithmListViewModel catalog) : base(catalog, "rbp") { }

    public IReadOnlyList<RbpBandPoint> Bands => preview.BandsAt(Catalog.DynamicPreviewCursorSeconds);
    public IReadOnlyList<RbpWindowPoint> Trend => preview.Windows
        .Where(window => window.EndSeconds <= Catalog.DynamicPreviewCursorSeconds + 1e-9).ToArray();
    public bool IsDynamic => Catalog.IsDynamicMode;
    public bool HasResult => Bands.Any(band => band.Share is not null);
    public string QualityText => Catalog.LastRun?.Error?.Message ?? (IsDynamic
        ? preview.Windows.LastOrDefault(window => window.EndSeconds <= Catalog.DynamicPreviewCursorSeconds + 1e-9) is { } current
            ? AlgorithmResultFormatter.FormatQualityStatus(current.Quality) : "尚未到达分析窗口"
        : string.IsNullOrWhiteSpace(preview.StaticQuality) ? "尚未生成结果" :
            AlgorithmResultFormatter.FormatQualityStatus(preview.StaticQuality));
    public int UnavailableWindowCount => preview.Windows.Count(window => window.State == "Unavailable");
    public int RejectedWindowCount => preview.Windows.Count(window => window.State == "Rejected");
    public string WindowStateSummary => !IsDynamic ? "静态分析" : preview.Windows.Length == 0 ? "未产生窗口结果" :
        string.Join("、", preview.Windows.GroupBy(window => window.State)
            .Select(group => $"{AlgorithmResultFormatter.FormatWindowStateForDisplay(group.Key)}：{group.Count()}"));
    private IEnumerable<RbpWindowPoint> ReleasedFailures => preview.Windows.Where(window =>
        window.EndSeconds <= Catalog.DynamicPreviewCursorSeconds + 1e-9 && !string.IsNullOrWhiteSpace(window.Failure));
    public string FailureReasonText => Catalog.LastRun?.Error?.Message ?? (!IsDynamic
        ? (string.IsNullOrWhiteSpace(preview.StaticFailure) ? "暂无失败原因" : preview.StaticFailure)
        : ReleasedFailures.LastOrDefault() is { } latest
            ? $"已有 {ReleasedFailures.Count()} 个失败窗口；最近原因：{AlgorithmResultFormatter.FormatFailureCodeForDisplay(latest.Failure)}"
            : "暂无失败原因");
    public string FailureDetailsText => Catalog.LastRun?.Error?.Message ?? (IsDynamic ?
        string.Join(Environment.NewLine, ReleasedFailures
            .Select(window => $"{window.StartSeconds:0.###}–{window.EndSeconds:0.###} s：{AlgorithmResultFormatter.FormatFailureCodeForDisplay(window.Failure)}"))
        : preview.StaticFailure);
    public bool IsFailureDetailsExpanded
    {
        get => isFailureDetailsExpanded;
        private set => SetProperty(ref isFailureDetailsExpanded, value);
    }
    public ICommand ToggleFailureDetailsCommand => new RelayCommand(() => IsFailureDetailsExpanded = !IsFailureDetailsExpanded);
    public string WindowText => !IsDynamic ? "选定分析区间" :
        preview.Windows.LastOrDefault(window => window.EndSeconds <= Catalog.DynamicPreviewCursorSeconds + 1e-9) is { } current
            ? $"{current.StartSeconds:0.###}–{current.EndSeconds:0.###} s · {AlgorithmResultFormatter.FormatWindowStateForDisplay(current.State)}"
            : "尚未到达分析窗口";

    protected override void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        base.OnCatalogPropertyChanged(sender, args);
        if (args.PropertyName == nameof(AlgorithmListViewModel.LastRun))
            preview = RbpResultPreview.Parse(Catalog.SelectedAlgorithm?.Id == "rbp" ? Catalog.LastRun : null);
        if (args.PropertyName is nameof(AlgorithmListViewModel.LastRun)
            or nameof(AlgorithmListViewModel.DynamicPreviewCursorSeconds)
            or nameof(AlgorithmListViewModel.IsDynamicMode))
        {
            RaisePropertyChanged(nameof(Bands));
            RaisePropertyChanged(nameof(Trend));
            RaisePropertyChanged(nameof(IsDynamic));
            RaisePropertyChanged(nameof(HasResult));
            RaisePropertyChanged(nameof(WindowText));
            RaisePropertyChanged(nameof(QualityText));
            RaisePropertyChanged(nameof(UnavailableWindowCount));
            RaisePropertyChanged(nameof(RejectedWindowCount));
            RaisePropertyChanged(nameof(WindowStateSummary));
            RaisePropertyChanged(nameof(FailureReasonText));
            RaisePropertyChanged(nameof(FailureDetailsText));
        }
    }
}
