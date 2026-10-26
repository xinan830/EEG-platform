using System.Reflection;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class TnkWorkbenchTests
{
    [Fact]
    public void OnDemandScoreStatesAccumulateReturnedWindowDurationsByThreshold()
    {
        var series = new TnkChartSeries("score", "score", [
            new(10, 50, "10 s"), new(11, 60, "11 s"), new(12, 70, "12 s"),
            new(13, 80, "13 s")]);

        var states = TnkOnDemandScoreStateProjection.Project(series, 13);

        Assert.Equal(new double[] { 0, 1, 1, 1 }, states.Select(state => state.DurationSeconds));
        Assert.Equal(new double[] { 0, 33.33333333, 33.33333333, 33.33333333 }, states.Select(state => Math.Round(state.Percent, 8)));
    }

    [Fact]
    public void OnDemandScoreStatesDoNotInterpolateAcrossMissingWindows()
    {
        var series = new TnkChartSeries("score", "score", [
            new(10, 80, "10 s"), new(11, null, "11 s"), new(15, 50, "15 s")]);

        var states = TnkOnDemandScoreStateProjection.Project(series, 15);

        Assert.Equal(0, states.Sum(state => state.DurationSeconds));
    }

    [Fact]
    public void Plot_panel_updates_data_without_replacing_the_panel_instance()
    {
        var panel = new TnkPlotPanel("趋势", new(true, []), false);
        var changes = 0;
        panel.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(TnkPlotPanel.Chart)) changes++; };

        panel.Update(new TnkPlotPanel("趋势", new(true, [new("Pz", "Hz", [new(4, 10, "4 s")])]), false));

        Assert.Equal(1, changes);
        Assert.Equal(10, panel.Chart.Series[0].Points[0].Value);
    }

    [Fact]
    public void WindowNavigationClampsTimeAndRebasesPlaybackClock()
    {
        var catalog = new AlgorithmListViewModel(new HttpAlgorithmClient(new System.Net.Http.HttpClient()), new OperationNotificationCenter());
        typeof(AlgorithmListViewModel).GetProperty("RegisteredRecording")!.SetValue(catalog,
            new RegisteredRecording("record", "record", ".wpf", "", 1000, 30, ["Fz-REF"], "source"));
        var model = new TnkDetailViewModel(catalog, "tnk_biofeedback");
        model.SeekFromChart(-10);
        Assert.Equal(0, model.WaveformPosition);
        model.NextWaveformCommand.Execute(null);
        Assert.Equal(10, model.WaveformPosition);
        Assert.Equal(10d, typeof(TnkDetailViewModel).GetField("playbackOrigin", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(model));
        model.PreviousWaveformCommand.Execute(null);
        Assert.Equal(0, model.WaveformPosition);
        model.SeekFromChart(100);
        Assert.Equal(29.999, model.WaveformPosition, 3);
        Assert.Equal(model.WaveformPosition, typeof(TnkDetailViewModel).GetField("playbackOrigin", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(model));
    }

    [Fact]
    public void RbpPanelsUseCanonicalBandOrderPercentUnitsAndPreserveGaps()
    {
        var child = new TnkChildResultDisplay("tnk_rbp", "RBP", "Fz", "Fz-REF", "", "良好", "")
        { Chart = new(true, new[] { "alpha", "beta", "delta", "theta" }.Select(band =>
            new TnkChartSeries($"Fz-REF · {band}", "ratio", [new(4, .2, "4 s"),new(5,null,"5 s")])).ToArray()) };
        var panel = Assert.Single(TnkDetailViewModel.BuildPlots([child],new("RBP",["tnk_rbp"],true)));
        Assert.True(panel.IsStacked);
        Assert.Equal(new[] {"delta","theta","alpha","beta"},panel.Chart.Series.Select(series=>series.Name));
        Assert.All(panel.Chart.Series,series=> { Assert.Equal("%",series.Unit); Assert.Equal(20,series.Points[0].Value); Assert.Null(series.Points[1].Value); });
    }

    [Fact]
    public void Rbp_panel_keeps_a_stacked_chart_with_a_single_returned_window()
    {
        var child = new TnkChildResultDisplay("tnk_rbp", "RBP", "Fz", "Fz-REF", "", "", "")
        {
            Chart = new(true, [
                new("Fz-REF · delta", "ratio", [new(4, .5, "4 s")]),
                new("Fz-REF · theta", "ratio", [new(4, .1, "4 s")]),
                new("Fz-REF · alpha", "ratio", [new(4, .3, "4 s")]),
                new("Fz-REF · beta", "ratio", [new(4, .1, "4 s")])])
        };

        var panel = Assert.Single(TnkDetailViewModel.BuildPlots([child], new("RBP", ["tnk_rbp"], true)));

        Assert.True(panel.IsStacked);
        Assert.All(panel.Chart.Series, series => Assert.Equal("%", series.Unit));
        Assert.Equal(new[] { "delta", "theta", "alpha", "beta" }, panel.Chart.Series.Select(series => series.Name));
    }

    [Fact]
    public void SingleWindowKeepsTimeAxisAndBrainbeatTabKeepsBothIndependentSeries()
    {
        var child = new TnkChildResultDisplay("tnk_rbp","RBP","Fz","Fz-REF","","良好","")
        { Chart = new(true,[new("Fz-REF · alpha","ratio",[new(10,.4,"10 s")])]) };
        var panel=Assert.Single(TnkDetailViewModel.BuildPlots([child],new("RBP",["tnk_rbp"],true)));
        Assert.True(panel.IsStacked); Assert.True(panel.Chart.IsTimeline);
        Assert.Equal(10, panel.Chart.Series[0].Points[0].X);
        var original=child with {AlgorithmId="tnk_brainbeat",DisplayName="BrainBeat"};
        var flat=child with {AlgorithmId="tnk_brainbeat_flat",DisplayName="BrainBeat Flat"};
        var brainbeat=Assert.Single(TnkDetailViewModel.BuildPlots([original,flat],new("脑负荷",[original.AlgorithmId,flat.AlgorithmId])));
        Assert.Equal(2,brainbeat.Chart.Series.Count);
        Assert.Contains("BrainBeat Flat",brainbeat.Chart.Series[1].Name);
        Assert.False(brainbeat.IsStacked); Assert.True(brainbeat.Chart.IsTimeline);
    }

    [Fact]
    public void EmptyFaaPanelExplainsThatDynamicCursorWindowsAreUnsupported()
    {
        var faa = new TnkChildResultDisplay("tnk_faa", "TNK 额叶 Alpha 不对称", "F3、F4", "F3-REF/F4-REF",
            "不可用", "不可用", "TNK FAA 需要完整事件段");

        var panel = Assert.Single(TnkDetailViewModel.BuildPlots([faa], new("额叶平衡", ["tnk_faa"])));

        Assert.False(panel.Chart.HasValues);
        Assert.Contains("完整配对事件段", panel.EmptyReason);
        Assert.Contains("不能进行动态游标计算", panel.EmptyReason);
    }

    [Fact]
    public void SelectedModuleSelectsOnlyItsSeriesAndNullDoesNotBecomeZero()
    {
        var catalog = new AlgorithmListViewModel(new HttpAlgorithmClient(new System.Net.Http.HttpClient()), new OperationNotificationCenter());
        var model = new TnkDetailViewModel(catalog, "tnk_biofeedback");
        var first = new TnkChildResultDisplay("a","A","Pz","Pz","10 Hz","良好","")
        { Chart = new(true,[new("Pz","Hz",[new(4,10,"4 s")])]) };
        var second = first with { AlgorithmId = "b", DisplayName = "B",
            Chart = new(true,[new("Fz","ratio",[new(4,null,"4 s")])]) };
        model.SelectedMetric = first;
        Assert.Equal("Pz",model.SelectedMetricSeries!.Name);
        Assert.Contains("10 Hz",model.SelectedValueText);
        model.SelectedMetric = second;
        Assert.Equal("Fz",model.SelectedMetricSeries!.Name);
        Assert.Equal("当前窗口不可用",model.SelectedValueText);
        model.SelectedMetric = null;
        Assert.Null(model.SelectedMetricSeries);
    }

    [Fact]
    public void SeekingBeforeFirstCompleteWindowDoesNotShowFinalWindowValue()
    {
        var catalog = new AlgorithmListViewModel(new HttpAlgorithmClient(new System.Net.Http.HttpClient()), new OperationNotificationCenter());
        var model = new TnkDetailViewModel(catalog, "tnk_biofeedback");
        model.SelectedMetric = new("a", "A", "Pz", "Pz", "10 Hz", "良好", "")
        { Chart = new(true, [new("Pz", "Hz", [new(4, 10, "4 s"), new(5, 11, "5 s")])]) };
        model.WaveformPosition = 0;
        Assert.Equal("此时间尚无完整计算窗口", model.SelectedValueText);
    }

    [Fact]
    public void CadencedMetricHoldsAcrossScheduledGapButHonorsComputedUnavailablePoint()
    {
        var chart = new TnkResultChart(true, [new("IAPF", "Hz", [
            new(30, 9.5, "30 s"), new(31, null, "31 s", IsScheduledGap: true),
            new(36, null, "36 s")])]);

        Assert.Equal(9.5, TnkDetailViewModel.SelectValueAtCursor(chart, 33.7, hasSelectedTime: true));
        Assert.Null(TnkDetailViewModel.SelectValueAtCursor(chart, 36.1, hasSelectedTime: true));
        Assert.Null(TnkDetailViewModel.SelectValueAtCursor(chart, 29.9, hasSelectedTime: true));
    }

    [Fact]
    public void LastValidCardValueCanBeShownWithItsTimeWithoutChangingCurrentWindowSemantics()
    {
        var catalog = new AlgorithmListViewModel(new HttpAlgorithmClient(new System.Net.Http.HttpClient()), new OperationNotificationCenter());
        var model = new TnkDetailViewModel(catalog, "tnk_biofeedback");
        typeof(TnkDetailViewModel).GetField("waveformPosition", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(model, 36.1d);
        typeof(TnkDetailViewModel).GetField("hasSelectedTime", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(model, true);
        typeof(TnkDetailViewModel).GetField("childResults", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(model,
            new[] { new TnkChildResultDisplay("tnk_aperiodic", "Aperiodic", "Fz", "Fz-REF", "", "不可用", "")
            { Chart = new(true, [new("Fz-REF", "exponent", [new(35, null, "35 s")]),
                new("Pz-REF", "exponent", [new(30, 1.73, "30 s"), new(35, null, "35 s")])]) } });

        Assert.Equal("1.73", model.CorticalValue);
        Assert.Equal("当前窗口不可用 · 最近有效 30 s", model.CorticalValueStatus);
        Assert.Equal("Pz-REF", model.CorticalSource);
    }

    [Fact]
    public void ReusableWaveformContractHasNoTnkOrHttpDependencies()
    {
        var properties = typeof(SignalPreviewResponse).GetProperties();
        Assert.Contains(properties,item=>item.Name=="TimeSeconds");
        Assert.Contains(properties,item=>item.Name=="Unit");
        Assert.DoesNotContain(properties,item=>item.PropertyType.Name.Contains("Tnk"));
        var dependencies = typeof(SignalWaveformView).GetFields(BindingFlags.NonPublic|BindingFlags.Instance);
        Assert.DoesNotContain(dependencies,item=>typeof(IAlgorithmClient).IsAssignableFrom(item.FieldType));
    }
}
