using System.Collections.ObjectModel;
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
        Catalog.PropertyChanged += OnCatalogPropertyChanged;
        if (Catalog.Projects is not null)
            Catalog.Projects.PropertyChanged += OnProjectsPropertyChanged;
    }

    public AlgorithmListViewModel Catalog { get; }
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
    public string StartSeconds { get => Catalog.StartSecondsText; set => Catalog.StartSecondsText = value; }
    public string EndSeconds { get => Catalog.EndSecondsText; set => Catalog.EndSecondsText = value; }
    public string RunStatus => Catalog.RunStatusText;
    public bool IsRunning => Catalog.IsRunActive;
    public string ResultSummary => Catalog.ResultSummaryText;
    public string Provenance => Catalog.ProvenanceText;

    private void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AlgorithmListViewModel.SelectedProjectRecording)
            or nameof(AlgorithmListViewModel.SelectedAlgorithm)
            or nameof(AlgorithmListViewModel.RegisteredChannels)
            or nameof(AlgorithmListViewModel.RegisteredRecording)
            or nameof(AlgorithmListViewModel.SelectedChannel)
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
            or nameof(AlgorithmListViewModel.RunStatusText)
            or nameof(AlgorithmListViewModel.ResultSummaryText)
            or nameof(AlgorithmListViewModel.ProvenanceText))
        {
            RaisePropertyChanged(nameof(HasProject));
            RaisePropertyChanged(nameof(HasCompletedRecording));
            RaisePropertyChanged(nameof(HasRegisteredRecording));
            RaisePropertyChanged(nameof(HasChannel));
            RaisePropertyChanged(nameof(SelectedChannel));
            RaisePropertyChanged(nameof(StartSeconds));
            RaisePropertyChanged(nameof(EndSeconds));
            RaisePropertyChanged(nameof(RunStatus));
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
