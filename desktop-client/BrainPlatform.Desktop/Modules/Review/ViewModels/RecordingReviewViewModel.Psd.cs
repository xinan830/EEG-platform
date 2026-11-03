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
    }
}
