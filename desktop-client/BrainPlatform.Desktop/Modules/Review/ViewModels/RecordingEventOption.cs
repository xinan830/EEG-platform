using System.Windows.Media;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed class RecordingEventOption : ObservableObject
{
    private string displayText;

    public RecordingEventOption(
        RecordingEvent recordingEvent,
        string displayText,
        string detailText,
        string timeText = "",
        string intervalBadge = "")
    {
        Event = recordingEvent;
        this.displayText = displayText;
        DetailText = detailText;
        TimeText = timeText;
        IntervalBadge = intervalBadge;
        ColorBrush = ResolveBrush(recordingEvent.DefinitionSnapshot.Color);
    }

    public RecordingEvent Event { get; }

    public string DetailText { get; }

    public string DisplayText
    {
        get => displayText;
        set => SetProperty(ref displayText, value);
    }

    public string Name => Event.DefinitionSnapshot.Name;

    public string TimeText { get; }

    public string IntervalBadge { get; }

    public bool HasIntervalBadge => !string.IsNullOrWhiteSpace(IntervalBadge);

    public string NoteText => Event.Note ?? string.Empty;

    public bool HasNote => !string.IsNullOrWhiteSpace(Event.Note);

    public bool CanEdit => Event.Source is EventSource.ManualButton or EventSource.KeyboardShortcut;

    public Brush ColorBrush { get; }

    private static Brush ResolveBrush(string? colorText)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(colorText))
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorText.Trim()));
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
            // Fall back to brand blue.
        }

        var fallback = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        fallback.Freeze();
        return fallback;
    }
}
