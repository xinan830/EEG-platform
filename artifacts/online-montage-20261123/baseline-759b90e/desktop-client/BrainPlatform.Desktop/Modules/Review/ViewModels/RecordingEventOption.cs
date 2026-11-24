namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed class RecordingEventOption(RecordingEvent recordingEvent, string displayText, string detailText) : ObservableObject
{
    private string displayText = displayText;
    public RecordingEvent Event { get; } = recordingEvent;
    public string DetailText { get; } = detailText;
    public string DisplayText { get => displayText; set => SetProperty(ref displayText, value); }
}
