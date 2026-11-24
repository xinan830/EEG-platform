using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;

public enum PsdChartKind { Spectrum, BandShare }

public sealed record PsdChartOption(PsdChartKind Kind, string Label);

/// <summary>
/// PSD-only detail state. Shared recording selection and execution remain in
/// the catalog/context so other algorithms can reuse the same workflow.
/// </summary>
public sealed partial class PsdDetailViewModel : ObservableObject, IAlgorithmQualitySource
{
    private readonly AlgorithmListViewModel catalog;
    private bool isFailureDetailsExpanded;
    private PsdChartOption selectedChart;

    internal PsdDetailViewModel(AlgorithmListViewModel catalog)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        selectedChart = ChartOptions[0];
        catalog.PropertyChanged += OnCatalogPropertyChanged;
        catalog.PsdPreviewState.PropertyChanged += OnPsdPreviewStateChanged;
        if (catalog.Projects is not null)
            catalog.Projects.PropertyChanged += OnProjectsPropertyChanged;
    }

    public AlgorithmListViewModel Catalog => catalog;
    public string AlgorithmId => "psd";
    public IReadOnlyList<PsdChartOption> ChartOptions { get; } =
    [
        new(PsdChartKind.Spectrum, "功率谱密度"),
        new(PsdChartKind.BandShare, "频段功率占比"),
    ];
    public PsdChartOption SelectedChart
    {
        get => selectedChart;
        set
        {
            if (value is null || !ChartOptions.Contains(value) || !SetProperty(ref selectedChart, value)) return;
            RaisePropertyChanged(nameof(IsSpectrumSelected));
            RaisePropertyChanged(nameof(IsBandShareSelected));
        }
    }
    public bool IsSpectrumSelected => SelectedChart.Kind == PsdChartKind.Spectrum;
    public bool IsBandShareSelected => SelectedChart.Kind == PsdChartKind.BandShare;
    public ProjectWorkspaceViewModel? Projects => catalog.Projects;
    public ResearchProject? SelectedProject
    {
        get => catalog.Projects?.SelectedProject;
        set { if (catalog.Projects is not null) catalog.Projects.SelectedProject = value; }
    }
    public AlgorithmCatalogItem? Algorithm => catalog.SelectedAlgorithm;
    public bool IsAvailable => string.Equals(Algorithm?.Id, "psd", StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<AlgorithmParameter> Parameters => Algorithm?.Parameters ?? [];
    public bool HasStructuredResult => catalog.PsdPreviewState.HasPreview;
    public ObservableCollection<PsdPreviewPoint?> PreviewPoints => catalog.PsdPreviewState.Points;
    public ObservableCollection<PsdBandSharePoint> BandSharePoints => catalog.PsdPreviewState.BandShares;
    public string ValueUnit => catalog.PsdPreviewState.ValueUnit;
    public string FrequencyRange => catalog.PsdPreviewState.FrequencyRangeText;
    public string ValueRange => catalog.PsdPreviewState.ValueRangeText;
    public IReadOnlyList<string> RegisteredChannels => catalog.RegisteredChannels;
    public IReadOnlyList<string> AnalysisModes => catalog.AnalysisModes;
    public IReadOnlyList<double> DynamicWindowOptions => catalog.DynamicWindowOptions;
    public ProjectRecordingRow? SelectedProjectRecording
    {
        get => catalog.SelectedProjectRecording;
        set => catalog.SelectedProjectRecording = value;
    }
    public string SelectedChannel
    {
        get => catalog.SelectedChannel;
        set => catalog.SelectedChannel = value;
    }
    public string StartSecondsText
    {
        get => catalog.StartSecondsText;
        set => catalog.StartSecondsText = value;
    }
    public string EndSecondsText
    {
        get => catalog.EndSecondsText;
        set => catalog.EndSecondsText = value;
    }
    public string SelectedAnalysisMode
    {
        get => catalog.SelectedAnalysisMode;
        set => catalog.SelectedAnalysisMode = value;
    }
    public double SelectedDynamicWindowSeconds
    {
        get => catalog.SelectedDynamicWindowSeconds;
        set => catalog.SelectedDynamicWindowSeconds = value;
    }
    public string DynamicStepText
    {
        get => catalog.DynamicStepText;
        set => catalog.DynamicStepText = value;
    }
    public RegisteredRecording? RegisteredRecording => catalog.RegisteredRecording;
    public string RunStatus => catalog.RunStatusText;
    public string ResultSummary => catalog.ResultSummaryText;
    public string Provenance => catalog.ProvenanceText;
    public string StructuredPreview => catalog.StructuredPreviewText;
    public ObservableCollection<DynamicWindowRow> DynamicWindowRows => catalog.DynamicWindowRows;
    public StructuredPreviewResponse? StructuredPreviewResult => catalog.PsdPreviewState.StructuredPreview;
    public string QualityText => WindowResult is { } current
        ? AlgorithmResultFormatter.FormatQualityForDisplay(JsonSerializer.SerializeToElement(new { status = current.Quality }))
        : catalog.PsdPreviewState.StructuredPreview?.Quality is { } quality
        ? AlgorithmResultFormatter.FormatQualityForDisplay(quality)
        : catalog.LastRun?.Error is { } error ? $"运行失败：{AlgorithmResultFormatter.FormatFailureCodeForDisplay(error.Code)}" : "尚未运行分析";
    public string WindowStateText => WindowResult is { } current ? $"当前窗口：{AlgorithmListViewModel.WindowStateLabel(current.State)}"
        : catalog.PsdPreviewState.StructuredPreview is not { } preview || preview.WindowStateCounts.Count == 0
        ? "暂无窗口状态"
        : string.Join("、", preview.WindowStateCounts.Select(item => $"{AlgorithmResultFormatter.FormatWindowStateForDisplay(item.Key)}：{item.Value}"));
    public AnalysisRunResponse? LastRun => catalog.LastRun;
    public bool IsRunActive => catalog.IsRunActive;
    public string RunIdText => WindowResult is { } current ? $"窗口 {current.WindowId}" : catalog.LastRun?.RunId ?? "未运行";
    public string RequestedRangeText => FormatRange(catalog.WindowRequestedRange ?? catalog.LastRun?.RequestedRange);
    public string ActualRangeText => FormatRange(WindowResult?.Range is { } range
        ? new TimeRange(range.StartSeconds, range.EndSeconds) : catalog.LastRun?.ActualRange);
    public string ResultTimeText => catalog.LastRun?.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "未运行";
    public string RunStartedAtText => catalog.LastRun?.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "未运行";
    public string RunEndedAtText => catalog.LastRun is { Status: "completed" or "failed" }
        ? catalog.LastRun.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff")
        : "未结束";
    public string RunDurationText => catalog.LastRun is null
        ? "未运行"
        : $"{Math.Max(0, (catalog.LastRun.UpdatedAt - catalog.LastRun.CreatedAt).TotalSeconds):0.###} s";
    public double RunProgressPercent => catalog.LastRun?.ProgressPercent ?? 0;
    public string RunProgressText => catalog.IsOnDemandAnalysis ? catalog.RunStatusText : catalog.LastRun?.ProgressText ?? "未运行";
    public ICommand CancelRunCommand => catalog.CancelRunCommand;
    public bool CanCancelRun => catalog.IsOnDemandAnalysis && catalog.IsRunActive || catalog.LastRun?.Status is "queued" or "running";
    public string FailureReasonText => WindowResult?.Failure is { } windowFailure
        ? $"{windowFailure.Code}：{windowFailure.Message}" : catalog.LastRun?.Error is { } error
        ? $"{AlgorithmResultFormatter.FormatFailureCodeForDisplay(error.Code)}：{error.Message}"
        : StructuredFailureSummary(catalog.PsdPreviewState.StructuredPreview?.Failure) ?? GetWindowReasonSummary(catalog.PsdPreviewState.StructuredPreview);
    public bool IsFailureDetailsExpanded
    {
        get => isFailureDetailsExpanded;
        private set => SetProperty(ref isFailureDetailsExpanded, value);
    }
    public bool HasFailureDetails => WindowResult?.Failure is not null || catalog.LastRun?.Error is not null
        || IsFailureObject(catalog.PsdPreviewState.StructuredPreview?.Failure)
        || catalog.PsdPreviewState.StructuredPreview?.Windows.Any(window => WindowState(window) is not "Complete") == true;
    public string FailureDetailsText => WindowResult is { } current ? WindowFailureDetails(current)
        : BuildFailureDetails(catalog.PsdPreviewState.StructuredPreview, catalog.LastRun?.Error);
    public int UnavailableWindowCount => WindowResult is { } current ? (current.State == "unavailable" ? 1 : 0)
        : catalog.PsdPreviewState.StructuredPreview?.WindowStateCounts.TryGetValue("Unavailable", out var count) == true ? count : 0;
    public int RejectedWindowCount => WindowResult is { } current ? (current.State == "rejected" ? 1 : 0)
        : catalog.PsdPreviewState.StructuredPreview?.WindowStateCounts.TryGetValue("Rejected", out var count) == true ? count : 0;
    public string WindowStateSummary => WindowResult is not null ? WindowStateText
        : catalog.PsdPreviewState.StructuredPreview is null ? "未产生窗口结果" : WindowStateText;
    public ICommand RegisterCommand => catalog.RegisterCommand;
    public ICommand RunCommand => catalog.RunCommand;
    public ICommand SelectStaticModeCommand => catalog.SelectStaticModeCommand;
    public ICommand SelectDynamicModeCommand => catalog.SelectDynamicModeCommand;
    public bool IsDynamicMode => catalog.IsDynamicMode;
    public bool IsDynamicModeAvailable => catalog.IsDynamicModeAvailable;
    public double DynamicTimeProgressPercent
    {
        get => catalog.DynamicTimeProgressPercent;
        set => catalog.DynamicTimeProgressPercent = value;
    }
    public string DynamicTimeProgressText => catalog.DynamicTimeProgressText;
    public string DynamicCurrentWindowText => catalog.DynamicCurrentWindowText;
    public ICommand ToggleFailureDetailsCommand => new RelayCommand(ToggleFailureDetails);

    private void ToggleFailureDetails() => IsFailureDetailsExpanded = !IsFailureDetailsExpanded;

    private void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AlgorithmListViewModel.TimeDisplay))
        {
            RaisePropertyChanged(nameof(RequestedRangeText));
            RaisePropertyChanged(nameof(ActualRangeText));
            return;
        }
        if (args.PropertyName is nameof(AlgorithmListViewModel.SelectedAlgorithm)
            or nameof(AlgorithmListViewModel.SelectedProjectRecording)
            or nameof(ProjectWorkspaceViewModel.SelectedProject)
            or nameof(AlgorithmListViewModel.RegisteredRecording)
            or nameof(AlgorithmListViewModel.RegisteredChannels)
            or nameof(AlgorithmListViewModel.SelectedChannel)
            or nameof(AlgorithmListViewModel.StartSecondsText)
            or nameof(AlgorithmListViewModel.EndSecondsText)
            or nameof(AlgorithmListViewModel.SelectedAnalysisMode)
            or nameof(AlgorithmListViewModel.SelectedDynamicWindowSeconds)
            or nameof(AlgorithmListViewModel.DynamicStepText)
            or nameof(AlgorithmListViewModel.RunStatusText)
            or nameof(AlgorithmListViewModel.ResultSummaryText)
            or nameof(AlgorithmListViewModel.ProvenanceText)
            or nameof(AlgorithmListViewModel.StructuredPreviewText)
            or nameof(AlgorithmListViewModel.DynamicWindowRows)
            or nameof(AlgorithmListViewModel.IsDynamicMode)
            or nameof(AlgorithmListViewModel.IsDynamicModeAvailable)
            or nameof(AlgorithmListViewModel.DynamicTimeProgressPercent)
            or nameof(AlgorithmListViewModel.DynamicTimeProgressText)
            or nameof(AlgorithmListViewModel.DynamicCurrentWindowText)
            or nameof(AlgorithmListViewModel.PsdPreviewPoints)
            or nameof(AlgorithmListViewModel.HasPsdPreview)
            or nameof(AlgorithmListViewModel.PsdValueUnit)
            or nameof(AlgorithmListViewModel.PsdFrequencyRangeText)
            or nameof(AlgorithmListViewModel.PsdValueRangeText)
            or nameof(AlgorithmListViewModel.PsdStructuredPreview)
            or nameof(AlgorithmListViewModel.LastRun)
            or nameof(AlgorithmListViewModel.DisplayedAnalysisWindow)
            or nameof(AlgorithmListViewModel.IsOnDemandAnalysis)
            or nameof(AlgorithmListViewModel.IsRunActive))
        {
            if (args.PropertyName is nameof(AlgorithmListViewModel.SelectedAlgorithm)
                or nameof(AlgorithmListViewModel.SelectedProjectRecording)
                or nameof(AlgorithmListViewModel.PsdStructuredPreview)
                or nameof(AlgorithmListViewModel.LastRun))
                IsFailureDetailsExpanded = false;
            RaisePropertyChanged(nameof(Algorithm));
            RaisePropertyChanged(nameof(IsAvailable));
            RaisePropertyChanged(nameof(Parameters));
            RaisePropertyChanged(nameof(HasStructuredResult));
            RaisePropertyChanged(nameof(PreviewPoints));
            RaisePropertyChanged(nameof(BandSharePoints));
            RaisePropertyChanged(nameof(ValueUnit));
            RaisePropertyChanged(nameof(FrequencyRange));
            RaisePropertyChanged(nameof(ValueRange));
            RaisePropertyChanged(nameof(SelectedProjectRecording));
            RaisePropertyChanged(nameof(SelectedProject));
            RaisePropertyChanged(nameof(RegisteredRecording));
            RaisePropertyChanged(nameof(RegisteredChannels));
            RaisePropertyChanged(nameof(SelectedChannel));
            RaisePropertyChanged(nameof(StartSecondsText));
            RaisePropertyChanged(nameof(EndSecondsText));
            RaisePropertyChanged(nameof(SelectedAnalysisMode));
            RaisePropertyChanged(nameof(SelectedDynamicWindowSeconds));
            RaisePropertyChanged(nameof(DynamicStepText));
            RaisePropertyChanged(nameof(RunStatus));
            RaisePropertyChanged(nameof(ResultSummary));
            RaisePropertyChanged(nameof(Provenance));
            RaisePropertyChanged(nameof(StructuredPreview));
            RaisePropertyChanged(nameof(DynamicWindowRows));
            RaisePropertyChanged(nameof(IsDynamicMode));
            RaisePropertyChanged(nameof(IsDynamicModeAvailable));
            RaisePropertyChanged(nameof(DynamicTimeProgressPercent));
            RaisePropertyChanged(nameof(DynamicTimeProgressText));
            RaisePropertyChanged(nameof(DynamicCurrentWindowText));
            RaisePropertyChanged(nameof(StructuredPreviewResult));
            RaisePropertyChanged(nameof(QualityText));
            RaisePropertyChanged(nameof(WindowStateText));
            RaisePropertyChanged(nameof(LastRun));
            RaisePropertyChanged(nameof(IsRunActive));
            RaisePropertyChanged(nameof(CanCancelRun));
            RaisePropertyChanged(nameof(RunIdText));
            RaisePropertyChanged(nameof(RequestedRangeText));
            RaisePropertyChanged(nameof(ActualRangeText));
            RaisePropertyChanged(nameof(ResultTimeText));
            RaisePropertyChanged(nameof(RunStartedAtText));
            RaisePropertyChanged(nameof(RunEndedAtText));
            RaisePropertyChanged(nameof(RunDurationText));
            RaisePropertyChanged(nameof(RunProgressPercent));
            RaisePropertyChanged(nameof(RunProgressText));
            RaisePropertyChanged(nameof(FailureReasonText));
            RaisePropertyChanged(nameof(HasFailureDetails));
            RaisePropertyChanged(nameof(FailureDetailsText));
            RaisePropertyChanged(nameof(UnavailableWindowCount));
            RaisePropertyChanged(nameof(RejectedWindowCount));
            RaisePropertyChanged(nameof(WindowStateSummary));
        }
    }

    private void OnPsdPreviewStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        RaisePropertyChanged(nameof(HasStructuredResult));
        RaisePropertyChanged(nameof(PreviewPoints));
        RaisePropertyChanged(nameof(BandSharePoints));
        RaisePropertyChanged(nameof(ValueUnit));
        RaisePropertyChanged(nameof(FrequencyRange));
        RaisePropertyChanged(nameof(ValueRange));
        RaisePropertyChanged(nameof(StructuredPreviewResult));
        RaisePropertyChanged(nameof(QualityText));
        RaisePropertyChanged(nameof(WindowStateText));
        RaisePropertyChanged(nameof(FailureReasonText));
        RaisePropertyChanged(nameof(HasFailureDetails));
        RaisePropertyChanged(nameof(FailureDetailsText));
        RaisePropertyChanged(nameof(UnavailableWindowCount));
        RaisePropertyChanged(nameof(RejectedWindowCount));
        RaisePropertyChanged(nameof(WindowStateSummary));
    }

    private void OnProjectsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ProjectWorkspaceViewModel.SelectedProject)
            or nameof(ProjectWorkspaceViewModel.CompletedRecordings))
        {
            RaisePropertyChanged(nameof(SelectedProject));
            RaisePropertyChanged(nameof(SelectedProjectRecording));
            RaisePropertyChanged(nameof(Projects));
        }
    }

    private string FormatRange(TimeRange? range) => range is null
        ? "未运行"
        : catalog.TimeDisplay.FormatRange(range.StartSeconds, range.EndSeconds);

}
