using System.Windows;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewEventTimeTests
{
    [Theory]
    [InlineData("00:00:13.740", 13.740)]
    [InlineData("27:10:00.001", 97800.001)]
    [InlineData("00:00:00.000", 0)]
    public void ElapsedTimeRoundTrips(string text, double seconds)
    {
        Assert.Equal(seconds, ReviewEventTime.Parse(text), 6);
        Assert.Equal(text, ReviewEventTime.Format(seconds));
    }

    [Theory]
    [InlineData("13.74")]
    [InlineData("00:60:00.000")]
    [InlineData("00:00:60.000")]
    [InlineData("00:00:13.7")]
    [InlineData("-1:00:00.000")]
    [InlineData("NaN")]
    [InlineData("")]
    public void InvalidTimeIsRejected(string text) =>
        Assert.Equal("event_time_format", Assert.Throws<EventValidationException>(() => ReviewEventTime.Parse(text)).Code);

    [Fact]
    public void RoundsToSamplesAndRejectsEndOrOverflowingIntervals()
    {
        Assert.Equal(1374, ReviewEventTime.ToSample(13.74, 100, 2000));
        Assert.Equal(2, ReviewEventTime.ToSample(0.003, 500, 10000));
        Assert.Equal(1500, ReviewEventTime.ToSample(15, 100, 2000, 500));
        foreach (var seconds in new[] { double.NaN, double.PositiveInfinity, -1, 20, 19.999 })
            Assert.Throws<EventValidationException>(() => ReviewEventTime.ToSample(seconds, 100, 2000));
        Assert.Throws<EventValidationException>(() => ReviewEventTime.ToSample(15.01, 100, 2000, 500));
    }

    [Fact]
    public void CoordinatesUseDisplayedFileTimeAndExcludeOutsidePlot()
    {
        var plot = new Rect(20, 10, 800, 300);
        Assert.True(ReviewEventPlotCoordinates.TryGetSeconds(plot, new Point(420, 100), 120, 130, out var seconds));
        Assert.Equal(125, seconds);
        Assert.True(ReviewEventPlotCoordinates.TryGetSeconds(plot, new Point(20, 10), 120, 130, out seconds));
        Assert.Equal(120, seconds);
        Assert.False(ReviewEventPlotCoordinates.TryGetSeconds(plot, new Point(19, 100), 120, 130, out _));
        Assert.False(ReviewEventPlotCoordinates.TryGetSeconds(plot, new Point(500, 311), 120, 130, out _));
        Assert.False(ReviewEventPlotCoordinates.TryGetSeconds(plot, new Point(500, 100), 130, 120, out _));
    }
}
