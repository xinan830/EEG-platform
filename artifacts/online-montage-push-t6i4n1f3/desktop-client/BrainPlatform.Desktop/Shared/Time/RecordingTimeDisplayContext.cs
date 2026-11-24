using System.ComponentModel;
using System.Globalization;
using System.Windows;

namespace BrainPlatform.Desktop.Shared.Time;

/// <summary>Formats one recording's coordinates using the shared display preference.</summary>
public sealed class RecordingTimeDisplayContext : ObservableObject
{
    private readonly RecordingTimeDisplaySettings settings;
    private DateTimeOffset? recordingStartUtc;

    public RecordingTimeDisplayContext(RecordingTimeDisplaySettings settings, DateTimeOffset? startUtc = null)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        recordingStartUtc = startUtc == default(DateTimeOffset) ? null : startUtc;
        PropertyChangedEventManager.AddHandler(settings, OnSettingsChanged, nameof(settings.Mode));
    }

    public DateTimeOffset? RecordingStartUtc
    {
        get => recordingStartUtc;
        set
        {
            if (SetProperty(ref recordingStartUtc, value == default(DateTimeOffset) ? null : value))
                NotifyDisplayChanged();
        }
    }

    public bool CanShowClock => RecordingStartUtc is not null;
    public RecordingTimeDisplayMode EffectiveMode => CanShowClock ? settings.Mode : RecordingTimeDisplayMode.Relative;
    public bool IsRecordTime
    {
        get => EffectiveMode == RecordingTimeDisplayMode.Relative;
        set { if (value) settings.Mode = RecordingTimeDisplayMode.Relative; }
    }
    public bool IsClockTime
    {
        get => EffectiveMode == RecordingTimeDisplayMode.Clock;
        set { if (value && CanShowClock) settings.Mode = RecordingTimeDisplayMode.Clock; }
    }
    public string AxisTitle => IsClockTime ? "采集时间" : "记录时间（s）";
    public string ClockAvailabilityText => CanShowClock
        ? "采集时间由记录起点与样本时间换算，不代表外部时钟同步。"
        : "记录缺少可靠的采集起点，只能显示记录时间。";

    public string FormatPoint(double seconds) => !double.IsFinite(seconds) ? "--"
        : IsClockTime ? FormatClock(seconds, "HH:mm:ss.fff") : FormatRelative(seconds);

    public string FormatAxis(double seconds) => !double.IsFinite(seconds) ? string.Empty
        : IsClockTime ? FormatClock(seconds, "HH:mm:ss.fff")
        : seconds.ToString("0.###", CultureInfo.InvariantCulture);

    public string FormatRange(double startSeconds, double endSeconds) => !double.IsFinite(startSeconds) || !double.IsFinite(endSeconds)
        ? "--" : IsClockTime
        ? $"{FormatPoint(startSeconds)} – {FormatPoint(endSeconds)}"
        : $"{startSeconds.ToString("0.###", CultureInfo.InvariantCulture)}–{FormatRelative(endSeconds)}";

    public string FormatCursor(double seconds)
    {
        if (!double.IsFinite(seconds)) return "--";
        if (!CanShowClock) return FormatRelative(seconds);
        var clock = FormatClock(seconds, "HH:mm:ss.fff");
        return IsClockTime ? $"{clock}（{FormatRelative(seconds)}）" : $"{FormatRelative(seconds)}（{clock}）";
    }

    private string FormatClock(double seconds, string format)
    {
        var start = RecordingStartUtc!.Value;
        var local = RecordingTimeMapper.ToLocalTime(start, seconds);
        if (local.Date != start.ToLocalTime().Date) format = "yyyy-MM-dd " + format;
        return local.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string FormatRelative(double seconds) =>
        seconds.ToString("0.###", CultureInfo.InvariantCulture) + " s";

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args) => NotifyDisplayChanged();

    private void NotifyDisplayChanged()
    {
        RaisePropertyChanged(nameof(EffectiveMode));
        RaisePropertyChanged(nameof(IsRecordTime));
        RaisePropertyChanged(nameof(IsClockTime));
        RaisePropertyChanged(nameof(CanShowClock));
        RaisePropertyChanged(nameof(ClockAvailabilityText));
        RaisePropertyChanged(nameof(AxisTitle));
    }
}
