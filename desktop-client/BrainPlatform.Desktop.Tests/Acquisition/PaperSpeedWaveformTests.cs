using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Views;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class PaperSpeedWaveformTests
{
    [Fact]
    public void Build_UsesFractionalPaperWindowDurationForTheTimeAxis()
    {
        var source = new LiveWaveformSource(
            new AcquisitionStreamMetadata(
                "test-device",
                "test",
                100,
                [
                    new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
                    new AcquisitionChannel(1, 1, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
                ],
                1,
                DateTimeOffset.UtcNow),
            [new AcquisitionBatch(0, 100, 1, new double[100], DateTimeOffset.UtcNow)],
            [new LiveDisplayChannel(0, 0, "F3", "Reference", true)]);

        var frame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(
            source,
            displayWindowSeconds: 2.5,
            horizontalPixels: 500));

        Assert.Equal(2.5, frame.DisplayWindowSeconds, precision: 12);
        Assert.Equal(250, frame.WindowSampleCount);
        Assert.Equal(1, frame.CursorSeconds, precision: 12);
    }
}
