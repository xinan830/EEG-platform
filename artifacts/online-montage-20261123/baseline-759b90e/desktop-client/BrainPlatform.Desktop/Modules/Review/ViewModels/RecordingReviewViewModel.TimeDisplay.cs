using System.ComponentModel;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed partial class RecordingReviewViewModel
{
    public RecordingTimeDisplayContext TimeDisplay { get; private set; } = null!;

    public string RecordingStartText => TimeDisplay.FormatPoint(0);
    public string RecordingEndText => TimeDisplay.FormatPoint(DurationSeconds);
    public string RecordingClockRangeText =>
        $"记录范围：{TimeDisplay.FormatRange(0, DurationSeconds)}；" + TimeDisplay.ClockAvailabilityText;
    public string PositionTimeText => TimeDisplay.FormatCursor(PositionSeconds);
    public string WindowTimeText => TimeDisplay.FormatRange(ViewportStartSeconds,
        Math.Min(DurationSeconds, ViewportStartSeconds + VisibleDurationSeconds));

    private void InitializeTimeDisplay(RecordingTimeDisplaySettings settings)
    {
        TimeDisplay = new RecordingTimeDisplayContext(settings, recordingStartUtc);
        TimeDisplay.PropertyChanged += OnTimeDisplayChanged;
    }

    private void OnTimeDisplayChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(RecordingTimeDisplayContext.EffectiveMode)) return;
        RaisePropertyChanged(nameof(TimeDisplay));
        RaisePropertyChanged(nameof(RecordingStartText));
        RaisePropertyChanged(nameof(RecordingEndText));
        RaisePropertyChanged(nameof(PositionTimeText));
        RaisePropertyChanged(nameof(WindowTimeText));
        RaisePropertyChanged(nameof(RecordingClockRangeText));
        foreach (var option in RecordingEventOptions)
            option.DisplayText = $"{TimeDisplay.FormatPoint(option.Event.StartSample / (double)samplingRateHz)} {option.Event.DefinitionSnapshot.Name}";
    }
}
