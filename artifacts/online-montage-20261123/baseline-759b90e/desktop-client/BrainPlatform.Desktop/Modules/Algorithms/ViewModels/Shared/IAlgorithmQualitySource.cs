using System.ComponentModel;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public interface IAlgorithmQualitySource : INotifyPropertyChanged
{
    string AlgorithmId { get; }
    bool IsAvailable { get; }
    string QualityText { get; }
    int UnavailableWindowCount { get; }
    int RejectedWindowCount { get; }
    string WindowStateSummary { get; }
    string FailureReasonText { get; }
    string FailureDetailsText { get; }
    bool IsFailureDetailsExpanded { get; }
    ICommand ToggleFailureDetailsCommand { get; }
}
