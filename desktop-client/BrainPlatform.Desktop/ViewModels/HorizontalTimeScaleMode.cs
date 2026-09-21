namespace BrainPlatform.Desktop.ViewModels;

/// <summary>
/// Selects the single control used for the display-only horizontal EEG scale.
/// It never changes acquired samples or their sample-counter time base.
/// </summary>
public enum HorizontalTimeScaleMode
{
    PaperSpeed,
    Timebase,
}
