using BrainPlatform.Desktop.Modules.Review.Waveform;
using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Tests.Views;

public sealed class RecordingTimeLabelProviderTests
{
    [Fact]
    public void FormatLabel_UsesRecordingStartAndRelativeSeconds()
    {
        var start = new DateTimeOffset(2026, 1, 1, 12, 30, 0, TimeSpan.Zero);
        var labels = new RecordingTimeLabelProvider { RecordingStartUtc = start };

        Assert.Equal($"{start.AddSeconds(12).ToLocalTime():HH:mm:ss}", labels.FormatLabel(11.7d));
    }

    [Fact]
    public void FormatLabel_CrossesMidnightWithoutResettingPosition()
    {
        var localStart = new DateTimeOffset(
            2026, 1, 1, 23, 59, 58, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 1, 1)));
        var labels = new RecordingTimeLabelProvider { RecordingStartUtc = localStart.ToUniversalTime() };

        Assert.Equal("00:00:01", labels.FormatLabel(3d));
    }

    [Fact]
    public void FormatLabel_MatchesTheLiveAxisAtAnAlignedWallClockTick()
    {
        var start = new DateTimeOffset(2026, 9, 24, 13, 21, 30, 387, TimeSpan.Zero);
        var labels = new RecordingTimeLabelProvider { RecordingStartUtc = start };
        var tick = WallClockSecondTickProvider.CalculateAlignedSeconds(0, 2, start)[0];

        Assert.Equal($"{start.AddSeconds(tick).ToLocalTime():HH:mm:ss}", labels.FormatLabel(tick));
    }
}
