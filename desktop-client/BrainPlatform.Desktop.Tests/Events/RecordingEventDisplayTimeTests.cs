using System.Globalization;

namespace BrainPlatform.Desktop.Tests.Events;

public sealed class RecordingEventDisplayTimeTests
{
    [Fact]
    public void Clock_time_uses_recording_sample_not_event_creation_time()
    {
        var startUtc = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        var eventItem = new RecordingEvent(
            "event", "recording", "definition", new EventDefinitionSnapshot("EO", "睁眼", "#2563EB", 1),
            EventSource.ManualButton, null, null, 1_250, 0,
            startUtc.AddDays(2), startUtc.AddDays(2));

        var result = RecordingEventDisplayTime.FormatMarker(eventItem, startUtc, 500);
        var expectedClock = startUtc.AddSeconds(2.5).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.CurrentCulture);

        Assert.Contains(expectedClock, result);
        Assert.Contains($"{2.5.ToString("0.###", CultureInfo.CurrentCulture)} 秒", result);
        Assert.DoesNotContain(startUtc.AddDays(2).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture), result);
    }

    [Fact]
    public void Clock_time_rejects_invalid_sample_rate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecordingEventDisplayTime.FormatClock(DateTimeOffset.UtcNow, 0, 0));
    }

    [Fact]
    public void Clock_time_preserves_subsecond_sample_position()
    {
        var startUtc = new DateTimeOffset(2026, 9, 24, 12, 0, 0, 375, TimeSpan.Zero);

        Assert.Equal($"约 {startUtc.AddSeconds(1.25).ToLocalTime():HH:mm:ss.fff}",
            RecordingEventDisplayTime.FormatClockTime(startUtc, 5_120, 4_096));
    }
}
