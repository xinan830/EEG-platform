using BrainPlatform.Desktop.Shared.Time;

namespace BrainPlatform.Desktop.Tests.Events;

public sealed class RecordingTimeDisplayTests
{
    [Fact]
    public void Clock_axis_keeps_fractional_origin_at_each_recording_second()
    {
        var local = new DateTimeOffset(2026, 10, 10, 10, 47, 21, 14,
            TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 10)));
        var context = new RecordingTimeDisplayContext(new RecordingTimeDisplaySettings(), local.ToUniversalTime());
        Assert.Equal("0", context.FormatAxis(0));
        Assert.Equal("1", context.FormatAxis(1));
        context.IsClockTime = true;
        Assert.Equal("10:47:21.014", context.FormatAxis(0));
        Assert.Equal("10:47:22.014", context.FormatAxis(1));
        Assert.Equal("10:47:22.514", context.FormatAxis(1.5));
    }

    [Fact]
    public void Shared_preference_changes_both_contexts_but_not_their_origins()
    {
        var settings = new RecordingTimeDisplaySettings();
        var start = DateTimeOffset.UtcNow;
        var review = new RecordingTimeDisplayContext(settings, start);
        var algorithm = new RecordingTimeDisplayContext(settings, start.AddHours(1));
        Assert.Equal("56.479 s", review.FormatPoint(56.479));
        review.IsClockTime = true;
        Assert.True(algorithm.IsClockTime);
        Assert.Equal(start, review.RecordingStartUtc);
        Assert.Equal(start.AddHours(1), algorithm.RecordingStartUtc);
        Assert.Contains("56.479 s", review.FormatCursor(56.479));
        Assert.Contains(start.AddSeconds(56.479).ToLocalTime().ToString("HH:mm:ss.fff"),
            review.FormatCursor(56.479));
    }

    [Fact]
    public void Unknown_origin_cannot_use_clock_or_created_time()
    {
        var settings = new RecordingTimeDisplaySettings { Mode = RecordingTimeDisplayMode.Clock };
        var context = new RecordingTimeDisplayContext(settings);
        context.IsClockTime = true;
        Assert.False(context.CanShowClock);
        Assert.True(context.IsRecordTime);
        Assert.Equal("17 s", context.FormatPoint(17));
        Assert.Equal("17 s", context.FormatCursor(17));
        Assert.Contains("缺少", context.ClockAvailabilityText);
    }

    [Fact]
    public void Midnight_labels_include_date_and_never_wrap_relative_seconds()
    {
        var local = new DateTimeOffset(2026, 10, 10, 23, 59, 58,
            TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 10)));
        var context = new RecordingTimeDisplayContext(new RecordingTimeDisplaySettings(), local.ToUniversalTime());
        context.IsClockTime = true;
        Assert.Equal("2026-10-11 00:00:01.000", context.FormatPoint(3));
        Assert.Equal("2026-10-11 00:00:01.000", context.FormatAxis(3));
        context.IsRecordTime = true;
        Assert.Equal("90000 s", context.FormatPoint(90000));
        Assert.Equal("38–45 s", context.FormatRange(38, 45));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Invalid_display_coordinate_is_not_fabricated(double seconds)
    {
        var context = new RecordingTimeDisplayContext(new RecordingTimeDisplaySettings(), DateTimeOffset.UtcNow);
        Assert.Equal("--", context.FormatPoint(seconds));
        context.IsClockTime = true;
        Assert.Equal("--", context.FormatPoint(seconds));
    }
}
