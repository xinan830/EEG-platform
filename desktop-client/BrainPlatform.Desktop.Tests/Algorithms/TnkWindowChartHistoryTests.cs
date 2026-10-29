using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;
using SciChart.Data.Model;
using Xunit.Abstractions;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class TnkWindowChartHistoryTests
{
    private readonly ITestOutputHelper output;

    public TnkWindowChartHistoryTests(ITestOutputHelper output) => this.output = output;
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
    public void PartialWindowIsRetainedAtItsActualAnalysisTime()
    {
        var history = new TnkWindowChartHistory();
        var partial = Window(10) with { State = "partial" };
        var result = history.Read(partial, [Card(10, null)], 1);

        Assert.Equal(10, result[0].Chart.Series[0].Points.Single().X);
        Assert.Null(result[0].Chart.Series[0].Points.Single().Value);
    }

    [Fact]
    public void DeliveryGapIsProjectedAtActualTimeAndDeduplicatedBySequenceRange()
    {
        var history = new TnkWindowChartHistory();
        var current = Card(12, 12.5);
        history.Add(Window(12), [current]);
        var gap = new TnkPlaybackGap(4, 6, 10000, 12000, 10, 12, true);
        history.AddGap(gap);
        history.AddGap(gap);

        var points = history.Project([current], 1)[0].Chart.Series[0].Points;
        Assert.Equal(new double[] { 10, 12 }, points.Select(point => point.X));
        Assert.Null(points[0].Value);
        Assert.Contains("投递断档", points[0].Label);
    }

    [Fact]
    public void Failed_frame_gap_stays_incomplete_until_its_canonical_range_is_recovered()
    {
        var history = new TnkWindowChartHistory();
        history.Add(Window(6), [Card(6, 6)]);
        history.AddGap(new TnkPlaybackGap(2, 2, 6000, 10000, 6, 10,
            true, "delivery", "TNK_PLAYBACK_PUBLISH_FAILED"));

        var missing = history.Project([Card(6, 6)], 1)[0].Chart.Series[0].Points;
        Assert.Contains("TNK_PLAYBACK_PUBLISH_FAILED", missing[^1].Label);
        Assert.Contains("补齐", history.CoverageText);

        history.MarkReconciliationFailed();
        Assert.Contains("不完整", history.CoverageText);

        history.Add(Window(10), [Card(10, 10)]);

        Assert.Contains("完整", history.CoverageText);
    }

    [Fact]
    public void Duplicate_window_result_does_not_request_another_history_projection()
    {
        var history = new TnkWindowChartHistory();
        var window = Window(12);
        var result = new[] { Card(12, 12.5) };

        Assert.True(history.Add(window, result));
        Assert.False(history.Add(window with { WindowId = "stream-event-45" }, result));
        Assert.Equal(12, history.LatestCompletedEndSeconds);
    }

    [Fact]
    public void Updated_duplicate_window_result_is_retained_for_history_reconciliation()
    {
        var history = new TnkWindowChartHistory();
        var window = Window(12);
        history.Add(window, [Card(12, null)]);

        Assert.True(history.Add(window with { State = "partial" }, [Card(12, 12.5)]));
        var projected = history.Project([Card(12, 12.5)], 1);

        Assert.Equal(12.5, Assert.Single(projected[0].Chart.Series[0].Points).Value);
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

    [Fact]
    public void Exact_delivery_gap_stays_incomplete_until_all_lost_events_are_recovered()
    {
        var history = new TnkWindowChartHistory();
        history.AddGap(new TnkPlaybackGap(4, 4, 6000, 10000, 6, 10, true));
        Assert.Contains("补齐", history.CoverageText);

        history.MarkReconciliationFailed();
        Assert.Contains("不完整", history.CoverageText);

        history.BeginReconciliation();

        history.Add(Window(10), [Card(10, 10)]);

        Assert.Contains("完整", history.CoverageText);
    }

    [Fact]
    public void Bounded_history_explicitly_reports_truncation()
    {
        var history = new TnkWindowChartHistory();
        for (var time = 10; time < 10 + TnkWindowChartHistory.MaximumWindows + 1; time++)
            history.Add(Window(time), [Card(time, time)]);

        Assert.Contains("不完整", history.CoverageText);
    }

    [Fact]
    public void Chart_append_planner_appends_only_for_unchanged_prefix_and_requests_rebuild_for_recovery()
    {
        var first = Enumerable.Range(0, 600).Select(time => new TnkChartPoint(time, time, "point")).ToArray();
        var append = first.Append(new TnkChartPoint(600, 600, "point")).ToArray();
        var recoveredEarlier = first.ToArray();
        recoveredEarlier[300] = new(300, 12, "recovered");

        Assert.Equal(-1, TnkChartAppendPlanner.FindAppendStart(first, first));
        Assert.Equal(600, TnkChartAppendPlanner.FindAppendStart(first, append));
        Assert.Null(TnkChartAppendPlanner.FindAppendStart(first, recoveredEarlier));
    }

    [Fact]
    public void Detail_view_reports_analysis_end_and_signed_lag_separately_from_cursor()
    {
        var catalog = new AlgorithmListViewModel(new HttpAlgorithmClient(new HttpClient()), new OperationNotificationCenter());
        typeof(AlgorithmListViewModel).GetProperty("RegisteredRecording")!.SetValue(catalog,
            new RegisteredRecording("record", "record", ".wpf", "", 1000, 30, ["Fz-REF"], "source"));
        var model = new TnkDetailViewModel(catalog, "tnk_biofeedback");
        var history = (TnkWindowChartHistory)typeof(TnkDetailViewModel)
            .GetField("windowHistory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(model)!;
        history.Add(Window(8), [Card(8, 8)]);
        model.WaveformPosition = 10;

        Assert.Equal("最近返回分析窗口：8 s", model.LatestAnalysisWindowText);
        Assert.Equal("算法滞后：2 s", model.AnalysisLagText);
        model.WaveformPosition = 7;
        Assert.Equal("分析结果领先游标：1 s", model.AnalysisLagText);
    }

    [Fact]
    public void Timeline_projection_and_append_planning_cost_are_measured_at_increasing_history_sizes()
    {
        foreach (var count in new[] { 100, 300, 600 })
        {
            var history = new TnkWindowChartHistory();
            for (var time = 10; time < 10 + count; time++) history.Add(Window(time), [Card(time, time)]);
            var current = history.Project([Card(9 + count, 9 + count)], 1);
            var line = current[0].Chart.Series[0].Points;
            var appended = line.Append(new TnkChartPoint(10 + count, count, "append")).ToArray();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (var repeat = 0; repeat < 500; repeat++) TnkChartAppendPlanner.FindAppendStart(line, appended);
            timer.Stop();
            var lineMicroseconds = timer.Elapsed.TotalMicroseconds / 500;

            var rbp = new TnkChildResultDisplay("tnk_rbp", "RBP", "Fz", "Fz", "", "", "")
            { Chart = new(true, new[] { "delta", "theta", "alpha", "beta" }.Select(band =>
                new TnkChartSeries($"Fz · {band}", "ratio", line)).ToArray()) };
            timer.Restart();
            for (var repeat = 0; repeat < 500; repeat++) TnkDetailViewModel.BuildPlots([rbp], new("RBP", ["tnk_rbp"], true));
            timer.Stop();
            var stackedProjectionMicroseconds = timer.Elapsed.TotalMicroseconds / 500;
            output.WriteLine($"history={count}; append_prefix_scan_us={lineMicroseconds:F2}; stacked_projection_us={stackedProjectionMicroseconds:F2}; stacked_series_replace=0 for prefix append");
        }
    }
}
