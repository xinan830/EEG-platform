using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class TnkWindowChartHistoryTests
{
    [Fact]
    public void StackedTimelineAdvancesWhenFollowingTheLatestWindow()
    {
        var advanced = TnkTimelineRange.AdvanceIfFollowing(new DoubleRange(0, 10.5), 10, 11);

        Assert.NotNull(advanced);
        Assert.Equal(.5, advanced.Min);
        Assert.Equal(11, advanced.Max);
    }

    [Fact]
    public void StackedTimelineDoesNotStealManualHistoricalPan()
    {
        var advanced = TnkTimelineRange.AdvanceIfFollowing(new DoubleRange(0, 8), 10, 11);

        Assert.Null(advanced);
    }

    private static TnkChildResultDisplay Card(double time, double? value) =>
        new("tnk_iapf", "IAPF", "Pz/Oz", "Pz/O2", "", "", "")
        { Chart = new(true, [new("Pz/O2", "Hz", [new(time, value, $"{time} s")])]) };
    private static DynamicWindowResponse Window(double time, string session = "ref") => new(
        session, $"window-{time}", "request", "ready",
        JsonSerializer.SerializeToElement(new { algorithm_id = "tnk_biofeedback" }),
        null, new((long)((time - 4) * 1000), (long)(time * 1000), time - 4, time), "clean", null, null);

    [Fact]
    public void VisitedWindowsUseActualTimesAndSkipUncomputedGaps()
    {
        var history = new TnkWindowChartHistory();
        history.Read(Window(10), [Card(10, 10.5)], 1);
        history.Read(Window(11), [Card(11, null)], 1);
        var chart = history.Read(Window(15), [Card(15, 11.5)], 1)[0].Chart;
        Assert.True(chart.IsTimeline);
        Assert.Equal(new double[] { 10, 11, 12, 15 }, chart.Series[0].Points.Select(point => point.X));
        Assert.Equal(new double?[] { 10.5, null, null, 11.5 }, chart.Series[0].Points.Select(point => point.Value));
    }

    [Fact]
    public void RepeatedAndBackwardWindowsStaySortedAndReplaceTheirOwnValues()
    {
        var history = new TnkWindowChartHistory();
        history.Read(Window(11), [Card(11, 11)], 1);
        history.Read(Window(10), [Card(10, 10)], 1);
        var points = history.Read(Window(11), [Card(11, 12)], 1)[0].Chart.Series[0].Points;
        Assert.Equal(new double[] { 10, 11 }, points.Select(point => point.X));
        Assert.Equal(12, points[1].Value);
    }

    [Fact]
    public void NewScientificSessionAndExplicitInvalidationClearOldValues()
    {
        var history = new TnkWindowChartHistory();
        history.Read(Window(10), [Card(10, 10)], 1);
        var changed = history.Read(Window(11, "average"), [Card(11, 12)], 1);
        Assert.Equal(11, Assert.Single(changed[0].Chart.Series[0].Points).X);
        history.Clear();
        var cleared = history.Read(Window(15, "average"), [Card(15, 13)], 1);
        Assert.Equal(15, Assert.Single(cleared[0].Chart.Series[0].Points).X);
    }

    [Fact]
    public void MissingResultAddsGapAndHistoryRemainsBounded()
    {
        var history = new TnkWindowChartHistory();
        IReadOnlyList<TnkChildResultDisplay> result = [];
        for (var time = 10; time <= 10 + TnkWindowChartHistory.MaximumWindows; time++)
            result = history.Read(Window(time), [Card(time, time)], 1);
        Assert.Equal(TnkWindowChartHistory.MaximumWindows, result[0].Chart.Series[0].Points.Count);
        var missing = Card(611, null) with { Chart = new(true, []) };
        result = history.Read(Window(611) with { State = "insufficient_data" }, [missing], 1);
        Assert.Null(result[0].Chart.Series[0].Points.Last().Value);
    }

    [Fact]
    public void Late_window_updates_trend_but_keeps_latest_current_value()
    {
        var history = new TnkWindowChartHistory();
        history.Add(Window(10), [Card(10, 10.5) with { ValueText = "10.5 Hz" }]);
        var current = Card(12, 12.5) with { ValueText = "12.5 Hz" };
        history.Add(Window(12), [current]);
        var projected = history.Project([current], 1);
        var displayed = Assert.Single(projected);

        Assert.Equal("12.5 Hz", displayed.ValueText);
        Assert.Equal(new double[] { 10, 11, 12 }, displayed.Chart.Series[0].Points.Select(point => point.X));
        Assert.Equal(new double?[] { 10.5, null, 12.5 }, displayed.Chart.Series[0].Points.Select(point => point.Value));
    }
}
