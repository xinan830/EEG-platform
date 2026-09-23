using BrainPlatform.Desktop.Modules.Review.Waveform;

namespace BrainPlatform.Desktop.Tests.Views;

public sealed class RecordingTimeLabelProviderTests
{
    [Fact]
    public void FormatLabel_UsesRecordingRelativeElapsedSeconds()
    {
        var labels = new RecordingTimeLabelProvider();

        Assert.Equal("11.7", labels.FormatLabel(11.7d));
    }
}
