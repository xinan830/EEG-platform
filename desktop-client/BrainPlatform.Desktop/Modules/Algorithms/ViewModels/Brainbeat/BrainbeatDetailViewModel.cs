using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Brainbeat;

public sealed class BrainbeatDetailViewModel : AlgorithmSpecificDetailViewModel, IAlgorithmQualitySource
{
    private BrainbeatResultPreview? preview;
    private bool isFailureDetailsExpanded;
    private string? windowSession;
    internal BrainbeatDetailViewModel(AlgorithmListViewModel catalog) : base(catalog, "brainbeat") => Refresh();
    public BrainbeatPoint? CurrentPoint => preview?.IsDynamic == true
        ? preview.Points.LastOrDefault(point => point.TimeSeconds <= Catalog.DynamicPreviewCursorSeconds + 1e-9)
        : preview?.Points.LastOrDefault();
    public string ValueText => preview?.Value is double value ? value.ToString("0.######") : CurrentPoint?.Value?.ToString("0.######") ?? "不可用";
    public string UnitText => preview?.Unit ?? "ratio";
    public string ChannelText => string.IsNullOrWhiteSpace(preview?.Channel) ? "Fz / Pz" : preview!.Channel;
    public string ResultText => preview is null ? "尚未生成结果" : preview.IsDynamic ? $"动态窗口：{preview.Points.Count} 个" : $"Brainbeat：{ValueText} {UnitText}";
    public IReadOnlyList<BrainbeatPoint> TrendPoints => preview?.IsDynamic == true
        ? preview.Points.Where(point => point.TimeSeconds <= Catalog.DynamicPreviewCursorSeconds + 1e-9).ToArray()
        : preview?.Points ?? [];
    public double TimelineStartSeconds => preview?.Points.FirstOrDefault() is { } first && double.IsFinite(first.StartSeconds) ? first.StartSeconds : preview?.Points.FirstOrDefault()?.TimeSeconds ?? 0;
    public double TimelineEndSeconds => preview?.Points.LastOrDefault() is { } last && double.IsFinite(last.EndSeconds) ? last.EndSeconds : preview?.Points.LastOrDefault()?.TimeSeconds ?? 1;
    public string FailureReasonText => CurrentPoint?.Failure is { Length: > 0 } failure ? failure : "暂无失败原因";
    public string FailureDetailsText => preview is null ? "暂无失败详情" : string.Join(Environment.NewLine, preview.Points.Where(point => point.Failure.Length > 0).Select(point => $"{point.TimeSeconds:0.###} s：{point.Failure}"));
    public int UnavailableWindowCount => ReleasedPoints.Count(point => string.Equals(point.State, "Unavailable", StringComparison.OrdinalIgnoreCase));
    public int RejectedWindowCount => ReleasedPoints.Count(point => string.Equals(point.State, "Rejected", StringComparison.OrdinalIgnoreCase) || string.Equals(point.Quality, "gate_failed", StringComparison.OrdinalIgnoreCase));
    public string WindowStateSummary => preview is not { IsDynamic: true } ? "静态分析" : !ReleasedPoints.Any() ? "暂无窗口状态" : string.Join("、", ReleasedPoints.GroupBy(point => point.State).Select(group => $"{AlgorithmResultFormatter.FormatWindowStateForDisplay(group.Key)}：{group.Count()}"));
    public string QualityText => Catalog.LastRun?.Error is not null ? "运行失败" : CurrentPoint is null ? "尚未到达分析窗口" : CurrentPoint is { Value: null } ? "未通过质量门" : "良好";
    private IEnumerable<BrainbeatPoint> ReleasedPoints => preview?.IsDynamic == true ? preview.Points.Where(point => point.TimeSeconds <= Catalog.DynamicPreviewCursorSeconds + 1e-9) : preview?.Points ?? [];
    public bool IsFailureDetailsExpanded { get => isFailureDetailsExpanded; private set => SetProperty(ref isFailureDetailsExpanded, value); }
    public ICommand ToggleFailureDetailsCommand => new RelayCommand(() => IsFailureDetailsExpanded = !IsFailureDetailsExpanded);
    protected override void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        base.OnCatalogPropertyChanged(sender, args);
        if (args.PropertyName is nameof(AlgorithmListViewModel.LastRun)
            or nameof(AlgorithmListViewModel.BackgroundFullRun)
            or nameof(AlgorithmListViewModel.SelectedAlgorithm)) Refresh();
        else if (args.PropertyName == nameof(AlgorithmListViewModel.DisplayedAnalysisWindow))
        {
            if (IsServingCompletedBackgroundFullRun)
            {
                RaiseResultProperties();
                return;
            }
            if (Algorithm?.Id == "brainbeat" && Catalog.DisplayedAnalysisWindow is { } response)
            {
                preview = BrainbeatResultPreview.FromWindow(response, SourceLabel(),
                    windowSession == response.SessionId ? preview : null);
                windowSession = response.SessionId;
            }
            else { windowSession = null; preview = null; }
            RaiseResultProperties();
        }
        else if (args.PropertyName == nameof(AlgorithmListViewModel.DynamicPreviewCursorSeconds)) RaiseResultProperties();
    }
    private void Refresh()
    {
        windowSession = null;
        preview = Algorithm?.Id == "brainbeat"
            ? CompletedBackgroundFullRun is { } fullRun
                ? BrainbeatResultPreview.Parse(fullRun)
                : Catalog.DisplayedAnalysisWindow is { } response
                ? BrainbeatResultPreview.FromWindow(response, SourceLabel())
                : BrainbeatResultPreview.Parse(Catalog.LastRun)
            : null;
        if (!IsServingCompletedBackgroundFullRun && Algorithm?.Id == "brainbeat" && Catalog.DisplayedAnalysisWindow is { } displayed)
            windowSession = displayed.SessionId;
        RaisePropertyChanged(nameof(CurrentPoint)); RaisePropertyChanged(nameof(ValueText)); RaisePropertyChanged(nameof(UnitText));
        RaisePropertyChanged(nameof(ChannelText)); RaisePropertyChanged(nameof(ResultText)); RaisePropertyChanged(nameof(TrendPoints));
        RaisePropertyChanged(nameof(TimelineStartSeconds)); RaisePropertyChanged(nameof(TimelineEndSeconds));
        RaiseResultProperties();
    }

    private string SourceLabel() =>
        string.IsNullOrWhiteSpace(Catalog.SelectedF4Channel)
            ? "Fz / Pz"
            : $"{Catalog.SelectedChannel} / {Catalog.SelectedF4Channel}";

    private void RaiseResultProperties()
    {
        foreach (var name in new[] { nameof(CurrentPoint), nameof(ValueText), nameof(ChannelText), nameof(ResultText), nameof(TrendPoints), nameof(TimelineStartSeconds), nameof(TimelineEndSeconds), nameof(FailureReasonText), nameof(FailureDetailsText), nameof(UnavailableWindowCount), nameof(RejectedWindowCount), nameof(WindowStateSummary), nameof(QualityText) })
            RaisePropertyChanged(name);
    }
}
