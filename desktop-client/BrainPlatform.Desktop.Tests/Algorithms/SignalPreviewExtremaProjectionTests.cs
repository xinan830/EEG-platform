using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class SignalPreviewExtremaProjectionTests
{
    [Fact]
    public void Builds_disconnected_minimum_to_maximum_segments_at_recording_bin_centers()
    {
        var projected = SignalPreviewExtremaProjection.ToVerticalSegments(
            [2.22, 2.27],
            new SignalPreviewChannelExtrema([-80, -10], [100, 30]));

        Assert.Equal([2.22, 2.22], projected.TimeSeconds.Take(2));
        Assert.Equal([-80, 100], projected.Microvolts.Take(2));
        Assert.Equal(2.22, projected.TimeSeconds[2]);
        Assert.True(double.IsNaN(projected.Microvolts[2]));
        Assert.Equal([2.27, 2.27], projected.TimeSeconds.Skip(3).Take(2));
        Assert.Equal([-10, 30], projected.Microvolts.Skip(3).Take(2));
        Assert.Equal(2.27, projected.TimeSeconds[5]);
    }

    [Fact]
    public void Rejects_extrema_that_do_not_match_the_bin_time_axis()
    {
        Assert.Throws<ArgumentException>(() => SignalPreviewExtremaProjection.ToVerticalSegments(
            [2.22], new SignalPreviewChannelExtrema([], [])));
    }
}
