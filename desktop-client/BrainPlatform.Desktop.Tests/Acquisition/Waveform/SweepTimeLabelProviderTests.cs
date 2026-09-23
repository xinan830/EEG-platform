using BrainPlatform.Desktop.Modules.Acquisition.Waveform;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class SweepTimeLabelProviderTests
{
    [Fact]
    public void FormatLabel_ShowsOnlyTicksReachedByTheEraseCursor()
    {
        var labels = new SweepTimeLabelProvider();
        labels.Update(pageStartSeconds: 0, currentCursorSeconds: 3.2);

        Assert.Equal("0", labels.FormatLabel(0d));
        Assert.Equal("3", labels.FormatLabel(3d));
        Assert.Equal(string.Empty, labels.FormatLabel(4d));
        Assert.Equal(string.Empty, labels.FormatLabel(10d));
    }

    [Fact]
    public void FormatLabel_AdvancesToTheCurrentPageElapsedTime()
    {
        var labels = new SweepTimeLabelProvider();
        labels.Update(pageStartSeconds: 10, currentCursorSeconds: 2.2);

        Assert.Equal("10", labels.FormatLabel(0d));
        Assert.Equal("12", labels.FormatLabel(2d));
        Assert.Equal(string.Empty, labels.FormatLabel(3d));
    }
}
