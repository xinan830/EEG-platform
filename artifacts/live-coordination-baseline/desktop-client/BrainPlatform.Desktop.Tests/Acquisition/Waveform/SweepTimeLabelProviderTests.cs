using BrainPlatform.Desktop.Modules.Acquisition.Waveform;
using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class SweepTimeLabelProviderTests
{
    [Fact]
    public void FormatLabel_ShowsOnlyTicksReachedByTheEraseCursor()
    {
        var labels = new SweepTimeLabelProvider();
        var anchor = new DateTimeOffset(2026, 1, 1, 12, 30, 0, TimeSpan.Zero);
        labels.Update(pageStartSeconds: 0, currentCursorSeconds: 3.2, anchor);

        Assert.Equal($"{anchor.ToLocalTime():HH:mm:ss}", labels.FormatLabel(0d));
        Assert.Equal($"{anchor.AddSeconds(3).ToLocalTime():HH:mm:ss}", labels.FormatLabel(3d));
        Assert.Equal(string.Empty, labels.FormatLabel(4d));
        Assert.Equal(string.Empty, labels.FormatLabel(10d));
    }

    [Fact]
    public void FormatLabel_AdvancesToTheCurrentPageElapsedTime()
    {
        var labels = new SweepTimeLabelProvider();
        var anchor = new DateTimeOffset(2026, 1, 1, 12, 30, 0, TimeSpan.Zero);
        labels.Update(pageStartSeconds: 10, currentCursorSeconds: 2.2, anchor);

        Assert.Equal($"{anchor.AddSeconds(10).ToLocalTime():HH:mm:ss}", labels.FormatLabel(0d));
        Assert.Equal($"{anchor.AddSeconds(12).ToLocalTime():HH:mm:ss}", labels.FormatLabel(2d));
        Assert.Equal(string.Empty, labels.FormatLabel(3d));
    }

    [Fact]
    public void FormatLabel_DoesNotInventAClockWithoutAnAnchor()
    {
        var labels = new SweepTimeLabelProvider();
        labels.Update(0, 2, null);

        Assert.Equal(string.Empty, labels.FormatLabel(1d));
    }

    [Fact]
    public void FormatLabel_UsesTheWholeSecondAtTheAlignedTick()
    {
        var anchor = new DateTimeOffset(2026, 9, 24, 13, 21, 30, 387, TimeSpan.Zero);
        var labels = new SweepTimeLabelProvider();
        labels.Update(0, 2, anchor);
        var tick = WallClockSecondTickProvider.CalculateAlignedSeconds(0, 2, anchor)[0];

        Assert.Equal($"{anchor.AddSeconds(tick).ToLocalTime():HH:mm:ss}", labels.FormatLabel(tick));
    }
}
