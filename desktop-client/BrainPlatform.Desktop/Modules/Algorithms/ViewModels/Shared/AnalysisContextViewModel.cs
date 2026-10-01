using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

/// <summary>
/// Shared analysis context exposed to every algorithm-specific detail module.
/// The catalog remains the single owner of selection and run state.
/// </summary>
public sealed class AnalysisContextViewModel : ObservableObject
{
    internal AnalysisContextViewModel(AlgorithmListViewModel catalog)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Parameters = new AlgorithmParameterContext(Catalog);
        Catalog.PropertyChanged += OnCatalogPropertyChanged;
        if (Catalog.Projects is not null)
            Catalog.Projects.PropertyChanged += OnProjectsPropertyChanged;
    }

    public AlgorithmListViewModel Catalog { get; }
    public AlgorithmParameterContext Parameters { get; }
    public ProjectWorkspaceViewModel? Projects => Catalog.Projects;
    public ResearchProject? SelectedProject { get => Catalog.Projects?.SelectedProject; set { if (Catalog.Projects is not null) Catalog.Projects.SelectedProject = value; } }
    public ProjectRecordingRow? SelectedProjectRecording { get => Catalog.SelectedProjectRecording; set => Catalog.SelectedProjectRecording = value; }
    public IReadOnlyList<ProjectRecordingRow> CompletedRecordings => Catalog.Projects?.CompletedRecordings ?? [];
    public IReadOnlyList<string> RegisteredChannels => Catalog.RegisteredChannels;
    public IReadOnlyList<string> AnalysisModes => Catalog.AnalysisModes;
    public IReadOnlyList<double> DynamicWindowOptions => Catalog.DynamicWindowOptions;
    public string DynamicStepText { get => Catalog.DynamicStepText; set => Catalog.DynamicStepText = value; }
    public double SelectedDynamicWindowSeconds { get => Catalog.SelectedDynamicWindowSeconds; set => Catalog.SelectedDynamicWindowSeconds = value; }
    public string SelectedAnalysisMode { get => Catalog.SelectedAnalysisMode; set => Catalog.SelectedAnalysisMode = value; }
    public bool IsDynamicMode => Catalog.IsDynamicMode;
    public bool IsDynamicModeAvailable => Catalog.IsDynamicModeAvailable;
    public bool IsDynamicPreviewPlaying => Catalog.IsDynamicPreviewPlaying;
    public double DynamicPreviewCursorSeconds => Catalog.DynamicPreviewCursorSeconds;
    public string DynamicPreviewCursorText => Catalog.DynamicPreviewCursorText;
    public bool HasDynamicPreview => Catalog.HasDynamicPreview;
    public string DynamicPreviewStatusText => Catalog.DynamicPreviewStatusText;
    public ObservableCollection<DynamicWindowRow> DynamicPreviewRows => Catalog.DynamicPreviewRows;
    public RegisteredRecording? RegisteredRecording => Catalog.RegisteredRecording;
    public ICommand RegisterCommand => Catalog.RegisterCommand;
    public ICommand RunCommand => Catalog.RunCommand;
    public ICommand SelectStaticModeCommand => Catalog.SelectStaticModeCommand;
    public ICommand SelectDynamicModeCommand => Catalog.SelectDynamicModeCommand;
    public ICommand StartDynamicPreviewCommand => Catalog.StartDynamicPreviewCommand;
    public ICommand PauseDynamicPreviewCommand => Catalog.PauseDynamicPreviewCommand;
    public ICommand StepDynamicPreviewCommand => Catalog.StepDynamicPreviewCommand;
    public ICommand ResetDynamicPreviewCommand => Catalog.ResetDynamicPreviewCommand;
    public bool HasProject => Catalog.Projects?.SelectedProject is not null;
    public bool HasCompletedRecording => Catalog.SelectedProjectRecording is not null;
    public bool HasRegisteredRecording => Catalog.RegisteredRecording is not null;
    public bool HasChannel => Catalog.HasRegisteredChannels;
    public string SelectedChannel { get => Catalog.SelectedChannel; set => Catalog.SelectedChannel = value; }
    public string SelectedF4Channel { get => Catalog.SelectedF4Channel; set => Catalog.SelectedF4Channel = value; }
    public string ChannelLabel => Catalog.ChannelLabel;
    public bool IsFaaSelected => Catalog.IsFaaSelected;
    public bool IsBrainbeatSelected => Catalog.IsBrainbeatSelected;
    public bool IsBandRatioSelected => Catalog.IsBandRatioSelected;
    public bool IsPeakFrequencySelected => Catalog.IsPeakFrequencySelected;
    public string StartSeconds { get => Catalog.StartSecondsText; set => Catalog.StartSecondsText = value; }
    public string EndSeconds { get => Catalog.EndSecondsText; set => Catalog.EndSecondsText = value; }
    public string RunStatus => Catalog.RunStatusText;
    public string RunIdText => Catalog.LastRun?.RunId ?? "未运行";
    public string RequestedRangeText => FormatRange(Catalog.LastRun?.RequestedRange);
    public string ActualRangeText => FormatRange(Catalog.LastRun?.ActualRange);
    public string ResultTimeText => Catalog.LastRun?.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "未运行";
    public string OutputUnit
    {
        get
        {
            var detailKind = Catalog.SelectedAlgorithm is { } algorithm
                ? AlgorithmUiRegistry.For(algorithm.Id).DetailKind
                : AlgorithmDetailKind.Scalar;
            return detailKind switch
            {
                AlgorithmDetailKind.FrequencySpectrum => Catalog.PsdValueUnit,
                AlgorithmDetailKind.TimeFrequency => Catalog.StftResult?.PowerUnit ?? "后端未提供",
                _ => MetricOutputUnit(Catalog.LastRun?.ResultSummary) ?? "后端未提供",
            };
        }
    }
    public string RecordingIdText => RegisteredRecording?.Id ?? "未注册";
    public string AlgorithmVersionText => Catalog.SelectedAlgorithm?.Version ?? "后端未提供";
    public string ImplementationVersionText => Catalog.SelectedAlgorithm?.ImplementationIdentity ?? "后端未提供";
    public string SelectedChannelText => SelectedChannel is { Length: > 0 } value ? value : "未选择";
    public string ParameterSnapshotText => "由本次 Run 固化";
    public string OutputUnitText => OutputUnit;
    public AnalysisContextViewModel Provenance => this;
    public string WindowStateText
    {
        get
        {
            var detailKind = Catalog.SelectedAlgorithm is { } algorithm
                ? AlgorithmUiRegistry.For(algorithm.Id).DetailKind
                : AlgorithmDetailKind.Scalar;
            if (detailKind == AlgorithmDetailKind.FrequencySpectrum)
                return Catalog.PsdWindowStateText;
            if (detailKind == AlgorithmDetailKind.TimeFrequency)
            {
                return Catalog.StftStructuredPreview is { } preview && preview.WindowStateCounts.Count > 0
                    ? string.Join("、", preview.WindowStateCounts.Select(item => $"{item.Key}：{item.Value}"))
                    : "尚未产生窗口结果";
            }
            return Catalog.DynamicWindowRows.Count > 0
                ? string.Join("、", Catalog.DynamicWindowRows.GroupBy(row => row.State)
                    .Select(group => $"{group.Key}：{group.Count()}"))
                : "尚未产生窗口结果";
        }
    }
    public double RunProgressPercent => Catalog.LastRun?.Status == "completed" ? 100 : 0;
    public string RunProgressText => Catalog.LastRun?.Status switch
    {
        "completed" => "100%",
        "queued" => "等待中",
        "running" => "运行中",
        "failed" => "失败",
        _ => "未运行",
    };
    public double DynamicTimeProgressPercent { get => Catalog.DynamicTimeProgressPercent; set => Catalog.DynamicTimeProgressPercent = value; }
    public string DynamicTimeProgressText => Catalog.DynamicTimeProgressText;
    public string DynamicCurrentWindowText => Catalog.DynamicCurrentWindowText;
    public bool IsRunning => Catalog.IsRunActive;
    public string ResultSummary => Catalog.ResultSummaryText;

    private void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AlgorithmListViewModel.SelectedProjectRecording)
            or nameof(AlgorithmListViewModel.SelectedAlgorithm)
            or nameof(AlgorithmListViewModel.RegisteredChannels)
            or nameof(AlgorithmListViewModel.RegisteredRecording)
            or nameof(AlgorithmListViewModel.SelectedChannel)
            or nameof(AlgorithmListViewModel.SelectedF4Channel)
            or nameof(AlgorithmListViewModel.StartSecondsText)
            or nameof(AlgorithmListViewModel.EndSecondsText)
            or nameof(AlgorithmListViewModel.SelectedAnalysisMode)
            or nameof(AlgorithmListViewModel.IsDynamicMode)
            or nameof(AlgorithmListViewModel.IsDynamicModeAvailable)
            or nameof(AlgorithmListViewModel.DynamicWindowOptions)
            or nameof(AlgorithmListViewModel.SelectedDynamicWindowSeconds)
            or nameof(AlgorithmListViewModel.DynamicStepText)
            or nameof(AlgorithmListViewModel.IsDynamicPreviewPlaying)
            or nameof(AlgorithmListViewModel.DynamicPreviewCursorSeconds)
            or nameof(AlgorithmListViewModel.DynamicPreviewCursorText)
            or nameof(AlgorithmListViewModel.HasDynamicPreview)
            or nameof(AlgorithmListViewModel.DynamicPreviewStatusText)
            or nameof(AlgorithmListViewModel.DynamicPreviewRows)
            or nameof(AlgorithmListViewModel.DynamicWindowRows)
            or nameof(AlgorithmListViewModel.RunStatusText)
            or nameof(AlgorithmListViewModel.LastRun)
            or nameof(AlgorithmListViewModel.DynamicTimeProgressPercent)
            or nameof(AlgorithmListViewModel.DynamicTimeProgressText)
            or nameof(AlgorithmListViewModel.DynamicCurrentWindowText)
            or nameof(AlgorithmListViewModel.ResultSummaryText)
            or nameof(AlgorithmListViewModel.ProvenanceText))
        {
            RaisePropertyChanged(nameof(HasProject));
            RaisePropertyChanged(nameof(HasCompletedRecording));
            RaisePropertyChanged(nameof(HasRegisteredRecording));
            RaisePropertyChanged(nameof(HasChannel));
            RaisePropertyChanged(nameof(SelectedChannel));
            RaisePropertyChanged(nameof(SelectedF4Channel));
            RaisePropertyChanged(nameof(ChannelLabel));
            RaisePropertyChanged(nameof(IsFaaSelected));
            RaisePropertyChanged(nameof(IsBrainbeatSelected));
            RaisePropertyChanged(nameof(IsBandRatioSelected));
            RaisePropertyChanged(nameof(IsPeakFrequencySelected));
            RaisePropertyChanged(nameof(StartSeconds));
            RaisePropertyChanged(nameof(EndSeconds));
            RaisePropertyChanged(nameof(RunStatus));
            RaisePropertyChanged(nameof(RunIdText));
            RaisePropertyChanged(nameof(RequestedRangeText));
            RaisePropertyChanged(nameof(ActualRangeText));
            RaisePropertyChanged(nameof(ResultTimeText));
            RaisePropertyChanged(nameof(OutputUnit));
            RaisePropertyChanged(nameof(RecordingIdText));
            RaisePropertyChanged(nameof(AlgorithmVersionText));
            RaisePropertyChanged(nameof(ImplementationVersionText));
            RaisePropertyChanged(nameof(SelectedChannelText));
            RaisePropertyChanged(nameof(ParameterSnapshotText));
            RaisePropertyChanged(nameof(OutputUnitText));
            RaisePropertyChanged(nameof(WindowStateText));
            RaisePropertyChanged(nameof(RunProgressPercent));
            RaisePropertyChanged(nameof(RunProgressText));
            RaisePropertyChanged(nameof(DynamicTimeProgressPercent));
            RaisePropertyChanged(nameof(DynamicTimeProgressText));
            RaisePropertyChanged(nameof(DynamicCurrentWindowText));
            RaisePropertyChanged(nameof(IsRunning));
            RaisePropertyChanged(nameof(ResultSummary));
            RaisePropertyChanged(nameof(Provenance));
            RaisePropertyChanged(nameof(Projects));
            RaisePropertyChanged(nameof(SelectedProject));
            RaisePropertyChanged(nameof(SelectedProjectRecording));
            RaisePropertyChanged(nameof(CompletedRecordings));
            RaisePropertyChanged(nameof(RegisteredChannels));
            RaisePropertyChanged(nameof(SelectedAnalysisMode));
            RaisePropertyChanged(nameof(DynamicStepText));
            RaisePropertyChanged(nameof(SelectedDynamicWindowSeconds));
            RaisePropertyChanged(nameof(DynamicWindowOptions));
            RaisePropertyChanged(nameof(IsDynamicMode));
            RaisePropertyChanged(nameof(IsDynamicModeAvailable));
            RaisePropertyChanged(nameof(IsDynamicPreviewPlaying));
            RaisePropertyChanged(nameof(DynamicPreviewCursorSeconds));
            RaisePropertyChanged(nameof(DynamicPreviewCursorText));
            RaisePropertyChanged(nameof(HasDynamicPreview));
            RaisePropertyChanged(nameof(DynamicPreviewStatusText));
            RaisePropertyChanged(nameof(DynamicPreviewRows));
            RaisePropertyChanged(nameof(RegisteredRecording));
        }
    }

    private static string FormatRange(TimeRange? range) => range is null
        ? "未运行"
        : $"{range.StartSeconds:0.###}–{range.EndSeconds:0.###} s";

    private static string? MetricOutputUnit(JsonElement? summary) =>
        summary is { ValueKind: JsonValueKind.Object } root &&
        root.TryGetProperty("metric", out var metric) && metric.ValueKind == JsonValueKind.Object &&
        metric.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Object &&
        output.TryGetProperty("unit", out var unit) && unit.ValueKind == JsonValueKind.String
            ? unit.GetString() : null;

    private void OnProjectsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ProjectWorkspaceViewModel.SelectedProject)
            or nameof(ProjectWorkspaceViewModel.CompletedRecordings))
        {
            RaisePropertyChanged(nameof(Projects));
            RaisePropertyChanged(nameof(SelectedProject));
            RaisePropertyChanged(nameof(CompletedRecordings));
            RaisePropertyChanged(nameof(HasProject));
            RaisePropertyChanged(nameof(HasCompletedRecording));
        }
    }
}
