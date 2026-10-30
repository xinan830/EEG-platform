using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;
using BrainPlatform.Desktop.Tests.Shared;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Annotations;
using SciChart.Charting.Visuals.RenderableSeries;

namespace BrainPlatform.Desktop.Tests.Algorithms;

[Collection("WPF application resources")]
public sealed class TnkChartLifecycleTests(WpfApplicationFixture fixture)
{
    [Theory]
    [InlineData(Visibility.Hidden)]
    [InlineData(Visibility.Collapsed)]
    public void Inactive_chart_ignores_updates_and_resumes_at_latest_snapshot(Visibility inactive) => fixture.Run(() =>
    {
        var panel = Panel(1);
        var plot = new TnkAnalysisPlotView { Panel = panel, PositionSeconds = 1 };
        var surface = Surface(plot);
        Assert.Empty(surface.RenderableSeries);
        var window = Host(plot);
        try
        {
            Assert.True(plot.IsLoaded && plot.IsVisible);
            var data = Assert.IsType<XyDataSeries<double, double>>(surface.RenderableSeries[0].DataSeries);
            var cursor = Assert.Single(surface.Annotations.OfType<VerticalLineAnnotation>());
            plot.Visibility = inactive;
            panel.Update(Panel(1, 2));
            plot.PositionSeconds = 2;
            Assert.Equal(1, data.Count);
            Assert.Equal(1d, cursor.X1);

            plot.Visibility = Visibility.Visible;
            Drain(window);
            Assert.Equal(2, data.Count);
            Assert.Equal(2d, cursor.X1);
            Assert.Same(panel, plot.Panel);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Unloaded_chart_ignores_old_and_replaced_panels_and_can_reload() => fixture.Run(() =>
    {
        var original = Panel(1);
        var plot = new TnkAnalysisPlotView { Panel = original, PositionSeconds = 1 };
        var surface = Surface(plot);
        var window = Host(plot);
        try
        {
            var data = Assert.IsType<XyDataSeries<double, double>>(surface.RenderableSeries[0].DataSeries);
            var cursor = Assert.Single(surface.Annotations.OfType<VerticalLineAnnotation>());
            for (var cycle = 0; cycle < 2; cycle++)
            {
                window.Content = null;
                Drain(window);
                Assert.False(plot.IsLoaded);
                var count = data.Count;
                var position = cursor.X1;
                original.Update(Panel(1, 2, 3, 4));
                var current = Panel(1, 2, 3);
                plot.Panel = current;
                current.Update(Panel(1, 2, 3, 4, 5));
                plot.PositionSeconds = 4 + cycle;
                Assert.Equal(count, data.Count);
                Assert.Equal(position, cursor.X1);

                window.Content = plot;
                Drain(window);
                Assert.True(plot.IsLoaded && plot.IsVisible);
                Assert.Equal(5, data.Count);
                Assert.Equal((double)(4 + cycle), cursor.X1);
                Assert.Same(current, plot.Panel);
            }
        }
        finally { window.Close(); }
    });

    private static TnkPlotPanel Panel(params double[] times) => new("fatigue",
        new TnkResultChart(true, [new TnkChartSeries("fatigue", "ratio",
            times.Select(time => new TnkChartPoint(time, time, "")).ToArray())]), false);

    private static SciChartSurface Surface(TnkAnalysisPlotView plot) =>
        ((DockPanel)plot.Content).Children.OfType<Grid>().Single().Children.OfType<SciChartSurface>().Single();

    private static Window Host(TnkAnalysisPlotView plot)
    {
        var window = new Window { Content = plot, Width = 700, Height = 450, ShowInTaskbar = false, Opacity = 0 };
        window.Show();
        Drain(window);
        return window;
    }

    [Fact]
    public void Rbp_panel_uses_one_surface_and_preserves_stacked_band_order() => fixture.Run(() =>
    {
        var panel = new TnkPlotPanel("C4-REF", new(true, [
            new("delta", "%", [new(1, 10, "1 s"), new(2, 11, "2 s")]),
            new("theta", "%", [new(1, 20, "1 s"), new(2, 21, "2 s")])]), true);
        var plot = new TnkAnalysisPlotView { Panel = panel, PositionSeconds = 2 };
        var surface = Surface(plot);
        var window = Host(plot);
        try
        {
            Assert.Equal(2, surface.RenderableSeries.Count);
            Assert.All(surface.RenderableSeries, series => Assert.IsType<StackedMountainRenderableSeries>(series));
            var stacked = surface.RenderableSeries.Cast<StackedMountainRenderableSeries>().ToArray();
            Assert.Equal(new[] { "bands", "bands" }, stacked.Select(series => series.StackedGroupId));
            Assert.Equal(["delta (%)", "theta (%)"],
                surface.RenderableSeries.Select(series => series.DataSeries.SeriesName));
        }
        finally { window.Close(); }
    });

    private static void Drain(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
    }
}
