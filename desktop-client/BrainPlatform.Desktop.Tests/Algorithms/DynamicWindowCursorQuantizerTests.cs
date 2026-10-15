using BrainPlatform.Desktop.Modules.Algorithms.Api;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class DynamicWindowCursorQuantizerTests
{
    [Theory]
    [InlineData(17.1, 17.0)]
    [InlineData(17.4, 17.0)]
    [InlineData(17.9, 17.0)]
    [InlineData(18.0, 18.0)]
    public void Quantizes_down_without_selecting_a_future_window(double seconds, double expectedSeconds)
    {
        var sample = DynamicWindowCursorQuantizer.QuantizeCursorSample(
            (long)(seconds * 1000), 1000, 1.0);

        Assert.Equal((long)(expectedSeconds * 1000), sample);
    }

    [Fact]
    public void Uses_analysis_start_as_the_quantization_anchor()
    {
        var sample = DynamicWindowCursorQuantizer.QuantizeCursorSample(
            13_900, 1000, 1.0, analysisStartSeconds: 2.5);

        Assert.Equal(13_500, sample);
    }

    [Fact]
    public void Does_not_move_cursor_before_analysis_start_backwards()
    {
        var sample = DynamicWindowCursorQuantizer.QuantizeCursorSample(
            2_400, 1000, 1.0, analysisStartSeconds: 2.5);

        Assert.Equal(2_400, sample);
    }
}
