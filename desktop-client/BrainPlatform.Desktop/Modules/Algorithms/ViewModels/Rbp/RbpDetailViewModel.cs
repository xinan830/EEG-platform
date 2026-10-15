using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;

public sealed class RbpDetailViewModel : AlgorithmSpecificDetailViewModel, IAlgorithmQualitySource
{
    private RbpResultPreview preview = RbpResultPreview.Parse(null);
    private bool isFailureDetailsExpanded;
    private string? windowSession;

    internal RbpDetailViewModel(AlgorithmListViewModel catalog) : base(catalog, "rbp")
    {
        if (catalog.SelectedAlgorithm?.Id != AlgorithmId) return;
        if (catalog.DisplayedAnalysisWindow is { } response)
        {
            preview = RbpResultPreview.FromWindow(response);
            windowSession = response.SessionId;
        }
        else preview = RbpResultPreview.Parse(catalog.LastRun);
    }

    public IReadOnlyList<RbpBandPoint> Bands => preview.BandsAt(Catalog.DynamicPreviewCursorSeconds);
    public IReadOnlyList<RbpWindowPoint> Trend => preview.Windows
        .Where(window => window.EndSeconds <= Catalog.DynamicPreviewCursorSeconds + 1e-9).ToArray();
    public bool IsDynamic => Catalog.IsDynamicMode;
    public bool HasResult => Bands.Any(band => band.Share is not null);
    public string QualityText => Catalog.DisplayedAnalysisWindow is { } response && Catalog.SelectedAlgorithm?.Id == AlgorithmId
        ? response.State == "ready" ? AlgorithmResultFormatter.FormatQualityStatus(response.Quality ?? "unknown")
            : AlgorithmListViewModel.WindowStateLabel(response.State)
        : Catalog.LastRun?.Error?.Message ?? (IsDynamic
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
            ? $"{Catalog.TimeDisplay.FormatRange(current.StartSeconds, current.EndSeconds)} · {AlgorithmResultFormatter.FormatWindowStateForDisplay(current.State)}"
            : "尚未到达分析窗口";

    protected override void OnTimeDisplayChanged() => RaisePropertyChanged(nameof(WindowText));

    protected override void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        base.OnCatalogPropertyChanged(sender, args);
        if (args.PropertyName == nameof(AlgorithmListViewModel.LastRun))
        {
            windowSession = null;
            preview = RbpResultPreview.Parse(Catalog.SelectedAlgorithm?.Id == "rbp" ? Catalog.LastRun : null);
        }
        if (args.PropertyName == nameof(AlgorithmListViewModel.DisplayedAnalysisWindow))
        {
            if (Catalog.SelectedAlgorithm?.Id == AlgorithmId && Catalog.DisplayedAnalysisWindow is { } response)
            {
                preview = RbpResultPreview.FromWindow(response, windowSession == response.SessionId ? preview : null);
                windowSession = response.SessionId;
            }
            else
            {
                windowSession = null;
                preview = RbpResultPreview.Parse(null);
            }
        }
        if (args.PropertyName is nameof(AlgorithmListViewModel.LastRun)
            or nameof(AlgorithmListViewModel.DisplayedAnalysisWindow)
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
