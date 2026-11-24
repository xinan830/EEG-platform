using BrainPlatform.Desktop.Modules.Acquisition.Waveform;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public sealed partial class LiveMonitoringViewModel
{
    private PreviewDisplayContinuity? previewDisplayContinuity;
    private LiveWaveformSource? frozenPreviewSource;
    private bool isDisplayFrozen;

    public bool IsDisplayFrozen => isDisplayFrozen;

    public void FreezePreviewDisplayForImpedance()
    {
        if (stateProvider().State != AcquisitionState.Previewing || isDisplayFrozen || GetWaveformSource() is not { } source)
        {
            return;
        }

        var windowSeconds = GetDisplayWindowSeconds(Math.Max(1d, displaySettings.ViewportWidthDips));
        previewDisplayContinuity = PreviewDisplayContinuity.Freeze(source, windowSeconds);
        frozenPreviewSource = previewDisplayContinuity?.FrozenSource;
        isDisplayFrozen = frozenPreviewSource is not null;
        RaisePropertyChanged(nameof(IsDisplayFrozen));
        Refresh();
    }

    public void ResumePreviewDisplay()
    {
        if (!isDisplayFrozen)
        {
            return;
        }

        isDisplayFrozen = false;
        frozenPreviewSource = null;
        Refresh();
        RaisePropertyChanged(nameof(IsDisplayFrozen));
    }

    public void ClearPreviewDisplayContinuity() => previewDisplayContinuity = null;
}
