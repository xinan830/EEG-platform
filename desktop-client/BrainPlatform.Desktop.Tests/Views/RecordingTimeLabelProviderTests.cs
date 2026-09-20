using BrainPlatform.Desktop.Views;

namespace BrainPlatform.Desktop.Tests.Views;

public sealed class RecordingTimeLabelProviderTests
{
    [Fact]
    public void FormatLabel_UsesRecordedStartTimeAndWindowOffset()
    {
        var labels = new RecordingTimeLabelProvider();
        labels.Update(new DateTimeOffset(2026, 9, 20, 0, 26, 37, TimeSpan.Zero), 10.2);

        Assert.Equal("08:26:48.7", labels.FormatLabel(1.5d));
    }
}
