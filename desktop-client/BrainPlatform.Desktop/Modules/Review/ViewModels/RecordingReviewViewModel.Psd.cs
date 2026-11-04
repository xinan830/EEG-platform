using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed partial class RecordingReviewViewModel
{
    public ReviewPsdParameters PsdParameters { get; } = new();
    public PsdReviewAnalysisViewModel PsdAnalysis { get; } = new();
    public ICommand OpenPsdAnalysisCommand { get; private set; } = null!;

    private void InitializePsdAnalysis()
    {
        OpenPsdAnalysisCommand = new AsyncRelayCommand(() => OpenAnalysisAsync("psd"), ReportCommandError);
        PsdParameters.PropertyChanged += OnAnalysisConfigurationChanged;
        PsdAnalysis.PropertyChanged += OnPsdPresentationChanged;
    }

    private void OnPsdPresentationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (activeAnalysisId != "psd") return;
        if (e.PropertyName == nameof(PsdReviewAnalysisViewModel.SelectedChannel))
            PsdAnalysis.Apply(CurrentAnalysisWindow);
        if (e.PropertyName is nameof(PsdReviewAnalysisViewModel.SelectedView) or
            nameof(PsdReviewAnalysisViewModel.SelectedFrequencyIndex))
        {
            if (PsdAnalysis.IsTopomapSelected) _ = RefreshPsdTopomapAsync();
            else CancelTopomapRequest();
        }
    }
}
