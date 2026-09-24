using BrainPlatform.Desktop.Shared.Time;
using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Tests.Events;

public sealed class RecordingTimeMapperTests
{
    [Fact]
    public void Sample_coordinate_preserves_submillisecond_precision_before_display()
    {
        var startUtc = new DateTimeOffset(2026, 9, 24, 13, 21, 30, 375, TimeSpan.Zero);

        var local = RecordingTimeMapper.ToLocalTime(startUtc, 5_121, 4_096);

        Assert.Equal(startUtc.AddTicks(12_502_441).ToLocalTime(), local);
        Assert.Equal(1.250244140625, RecordingTimeMapper.ToElapsedSeconds(5_121, 4_096), 12);
        Assert.Equal(local.ToString("HH:mm:ss.fff"),
            RecordingClockLabelFormatter.FormatSampleMilliseconds(startUtc, 5_121, 4_096));
    }

    [Fact]
    public void Invalid_sample_coordinates_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordingTimeMapper.ToLocalTime(DateTimeOffset.UtcNow, -1, 4_096));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordingTimeMapper.ToElapsedSeconds(0, 0));
    }
}
