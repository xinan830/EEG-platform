using System.ComponentModel;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed class QualityStatusViewModel : ObservableObject
{
    private readonly PsdDetailViewModel psd;
    private readonly StftDetailViewModel stft;

    internal QualityStatusViewModel(PsdDetailViewModel psd, StftDetailViewModel stft)
    {
        this.psd = psd;
        this.stft = stft;
        psd.PropertyChanged += OnDetailChanged;
        stft.PropertyChanged += OnDetailChanged;
    }

    public bool IsAvailable => psd.IsAvailable || stft.IsAvailable;
    public string QualityText => stft.IsAvailable ? stft.QualityText : psd.QualityText;
    public int UnavailableWindowCount => stft.IsAvailable ? stft.UnavailableWindowCount : psd.UnavailableWindowCount;
    public int RejectedWindowCount => stft.IsAvailable ? stft.RejectedWindowCount : psd.RejectedWindowCount;
    public string WindowStateSummary => stft.IsAvailable ? stft.WindowStateSummary : psd.WindowStateSummary;
    public string FailureReasonText => stft.IsAvailable ? stft.FailureReasonText : psd.FailureReasonText;
    public string FailureDetailsText => stft.IsAvailable ? stft.FailureDetailsText : psd.FailureDetailsText;
    public bool IsFailureDetailsExpanded => stft.IsAvailable ? stft.IsFailureDetailsExpanded : psd.IsFailureDetailsExpanded;
    public ICommand ToggleFailureDetailsCommand => stft.IsAvailable ? stft.ToggleFailureDetailsCommand : psd.ToggleFailureDetailsCommand;

    private void OnDetailChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender == psd && stft.IsAvailable || sender == stft && !stft.IsAvailable) return;
        RaisePropertyChanged(nameof(IsAvailable));
        RaisePropertyChanged(nameof(QualityText));
        RaisePropertyChanged(nameof(UnavailableWindowCount));
        RaisePropertyChanged(nameof(RejectedWindowCount));
        RaisePropertyChanged(nameof(WindowStateSummary));
        RaisePropertyChanged(nameof(FailureReasonText));
        RaisePropertyChanged(nameof(FailureDetailsText));
        RaisePropertyChanged(nameof(IsFailureDetailsExpanded));
        RaisePropertyChanged(nameof(ToggleFailureDetailsCommand));
    }
}
