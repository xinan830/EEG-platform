namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel
{
    public RecordingTimeDisplayContext TimeDisplay { get; private set; } = null!;

    private void InitializeTimeDisplay(RecordingTimeDisplaySettings settings)
    {
        TimeDisplay = new RecordingTimeDisplayContext(settings);
        TimeDisplay.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(RecordingTimeDisplayContext.EffectiveMode)) return;
            RaisePropertyChanged(nameof(TimeDisplay));
            RaisePropertyChanged(nameof(DynamicPreviewCursorText));
            RaisePropertyChanged(nameof(DynamicTimeProgressText));
            RaisePropertyChanged(nameof(DynamicCurrentWindowText));
        };
    }
}
