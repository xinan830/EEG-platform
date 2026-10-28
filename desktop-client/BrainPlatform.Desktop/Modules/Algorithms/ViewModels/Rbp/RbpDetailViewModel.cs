using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;

public sealed class RbpDetailViewModel : AlgorithmSpecificDetailViewModel, IAlgorithmQualitySource
{
    private RbpResultPreview preview = RbpResultPreview.Parse(null);
    private bool isFailureDetailsExpanded;
    private string? windowSession;
    private IReadOnlyList<RbpChannelHistory> channelHistories = [];

    internal RbpDetailViewModel(AlgorithmListViewModel catalog) : base(catalog, "rbp")
    {
        if (catalog.SelectedAlgorithm?.Id != AlgorithmId) return;
        if (CompletedBackgroundFullRun is { } fullRun)
            preview = RbpResultPreview.Parse(fullRun);
        else if (catalog.DisplayedAnalysisWindow is { } response)
        {
            preview = RbpResultPreview.FromWindow(response);
            windowSession = response.SessionId;
        }
        else preview = RbpResultPreview.Parse(catalog.LastRun);
    }

    public IReadOnlyList<RbpBandPoint> Bands => preview.BandsAt(Catalog.DynamicPreviewCursorSeconds);
    public IReadOnlyList<RbpWindowPoint> Trend =>
        preview.Windows.Where(window => !IsDynamic || window.EndSeconds <= Catalog.DynamicPreviewCursorSeconds + 1e-9).ToArray();
    public IReadOnlyList<RbpChannelHistory> ChannelHistories => channelHistories;
    public bool IsDynamic => Catalog.IsDynamicMode;
    public bool HasResult => Bands.Any(band => band.Share is not null);
    public string QualityText => !IsServingCompletedBackgroundFullRun && Catalog.DisplayedAnalysisWindow is { } response && Catalog.SelectedAlgorithm?.Id == AlgorithmId
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
        if (args.PropertyName is nameof(AlgorithmListViewModel.LastRun)
            or nameof(AlgorithmListViewModel.BackgroundFullRun))
        {
            windowSession = null;
            preview = Catalog.SelectedAlgorithm?.Id == "rbp"
                ? CompletedBackgroundFullRun is { } fullRun
                    ? RbpResultPreview.Parse(fullRun)
                    : RbpResultPreview.Parse(Catalog.LastRun)
                : RbpResultPreview.Parse(null);
            channelHistories = [];
            RaisePropertyChanged(nameof(ChannelHistories));
            if (Catalog.LastRun is { Status: "completed", ScientificVersion: "official-rbp-v3" } run)
                _ = LoadStructuredHistoryAsync(run.RunId);
        }
        if (args.PropertyName == nameof(AlgorithmListViewModel.DisplayedAnalysisWindow))
        {
            if (IsServingCompletedBackgroundFullRun)
            {
                RaiseRbpProperties();
                return;
            }
            if (Catalog.SelectedAlgorithm?.Id == AlgorithmId && Catalog.DisplayedAnalysisWindow is { } response)
            {
                preview = RbpResultPreview.FromWindow(response, windowSession == response.SessionId ? preview : null);
                windowSession = response.SessionId;
                if (channelHistories.Count == 0)
                {
                    channelHistories = RbpHistoryProjection.FromVisited(preview.ChannelWindows);
                    RaisePropertyChanged(nameof(ChannelHistories));
                }
                else
                {
                    var latestByChannel = preview.ChannelWindows
                        .GroupBy(point => point.Channel, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.Last());
                    RbpHistoryProjection.AppendVisited(channelHistories, latestByChannel,
                        RbpResultPreview.MaximumVisitedWindows);
                }
            }
            else
            {
                windowSession = null;
                preview = RbpResultPreview.Parse(null);
                channelHistories = [];
                RaisePropertyChanged(nameof(ChannelHistories));
            }
        }
        if ((args.PropertyName is nameof(AlgorithmListViewModel.LastRun)
            or nameof(AlgorithmListViewModel.BackgroundFullRun)
            or nameof(AlgorithmListViewModel.DisplayedAnalysisWindow)
            or nameof(AlgorithmListViewModel.DynamicPreviewCursorSeconds)
            or nameof(AlgorithmListViewModel.IsDynamicMode)) &&
            (args.PropertyName != nameof(AlgorithmListViewModel.DynamicPreviewCursorSeconds) || !Catalog.IsDynamicPreviewSeeking))
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

    private async Task LoadStructuredHistoryAsync(string runId)
    {
        try
        {
            var structured = await Catalog.Client.GetStructuredPreviewAsync(runId, 1_000_000, CancellationToken.None);
            if (Catalog.LastRun?.RunId != runId || Catalog.SelectedAlgorithm?.Id != AlgorithmId) return;
            preview = RbpResultPreview.FromStructured(structured);
            channelHistories = RbpHistoryProjection.FromFull(preview.ChannelWindows);
            if (IsDynamic && Catalog.DynamicWindowRows.Count == 0 && preview.Windows.Length > 0)
            {
                var rows = preview.Windows.Select(window => new DynamicWindowRow(window.StartSeconds, window.EndSeconds,
                    AlgorithmResultFormatter.FormatWindowStateForDisplay(window.State),
                    AlgorithmResultFormatter.FormatQualityReasonForDisplay(window.Quality), window.Failure));
                var initialCursor = preview.Windows.FirstOrDefault(window => window.State is "Complete" or "Partial")?.EndSeconds ?? 0;
                Catalog.SetDynamicResultWindows(rows, initialCursor);
            }
            RaisePropertyChanged(nameof(ChannelHistories));
            RaiseRbpProperties();
        }
        catch (Exception error)
        {
            if (Catalog.LastRun?.RunId != runId) return;
            channelHistories = [];
            Catalog.SetStructuredPreviewTextForModule($"RBP 多导联历史预览不可用：{error.Message}");
            RaisePropertyChanged(nameof(ChannelHistories));
        }
    }

    private void RaiseRbpProperties()
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
