using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Rbp;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using BrainPlatform.Desktop.Shared.Controls.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RbpReviewChartTests
{
    [Fact]
    public void Prepared_empty_chart_receives_first_window_without_rebuilding_series() => ReviewTimelineNavigatorTests.OnSta(() =>
    {
        var model = new RbpReviewAnalysisViewModel();
        model.InitializeChannels(Enumerable.Range(1, 21).Select(index => $"CH{index}"));
        var history = model.ChannelHistories[0];
        var chart = new RbpChart { IsStackedHistory = true, Bands = history.Bands, Windows = history.Windows };
        Load(chart);
        var surface = Assert.IsType<SciChartSurface>(chart.Content);
        var series = surface.RenderableSeries.Select(item => item.DataSeries).ToArray();
        Assert.Equal(5, series.Length);
        Assert.All(series, item => Assert.Empty(Assert.IsType<XyDataSeries<double, double>>(item).XValues));

        model.Apply(new WindowPreviewResponse("session", "recording", 600, 3, "insufficient_data", [],
            null, false, null, null, null, null, null, null, "rbp-fixture", "fixture"), 3);
        model.Apply(RbpReviewPresentationTests.Ready(5), 5);

        for (var index = 0; index < series.Length; index++)
        {
            Assert.Same(series[index], surface.RenderableSeries[index].DataSeries);
            Assert.Equal(new[] { 5d }, Assert.IsType<XyDataSeries<double, double>>(series[index]).XValues);
        }
    });

    [Fact]
    public void New_window_appends_to_existing_chart_series() => ReviewTimelineNavigatorTests.OnSta(() =>
    {
        var model = new RbpReviewAnalysisViewModel();
        model.Apply(RbpReviewPresentationTests.Ready(5), 5);
        var history = model.ChannelHistories[0];
        var chart = new RbpChart { IsStackedHistory = true, Bands = history.Bands, Windows = history.Windows };
        var surface = Assert.IsType<SciChartSurface>(chart.Content);
        Assert.Empty(surface.RenderableSeries);
        Load(chart);
        Assert.Equal("窗口结束时间（s）", Assert.Single(surface.XAxes).AxisTitle);
        Assert.Equal("相对功率（%）", Assert.Single(surface.YAxes).AxisTitle);
        var series = surface.RenderableSeries.ToArray();
        var data = series.Select(s => s.DataSeries).ToArray();

        model.Apply(RbpReviewPresentationTests.Ready(6), 6);

        for (var i = 0; i < 5; i++)
        {
            Assert.Same(series[i], surface.RenderableSeries[i]);
            Assert.Same(data[i], surface.RenderableSeries[i].DataSeries);
            var xy = Assert.IsType<XyDataSeries<double, double>>(data[i]);
            Assert.Equal(new[] { 5d, 6d }, xy.XValues);
        }
    });

    [Fact]
    public void Stacked_history_uses_review_panel_open_time_as_visible_left_edge() => ReviewTimelineNavigatorTests.OnSta(() =>
    {
        var model = new RbpReviewAnalysisViewModel();
        model.Apply(RbpReviewPresentationTests.Ready(30), 30);
        var history = model.ChannelHistories[0];
        var chart = new RbpChart
        {
            IsStackedHistory = true,
            VisibleStartSeconds = 30,
            Bands = history.Bands,
            Windows = history.Windows,
        };
        var surface = Assert.IsType<SciChartSurface>(chart.Content);
        Load(chart);
        var xAxis = Assert.Single(surface.XAxes);
        var initial = Assert.IsType<SciChart.Data.Model.DoubleRange>(xAxis.VisibleRange);
        Assert.Equal(30, initial.Min);
        Assert.Equal(31, initial.Max);

        model.Apply(RbpReviewPresentationTests.Ready(31), 31);

        var advanced = Assert.IsType<SciChart.Data.Model.DoubleRange>(xAxis.VisibleRange);
        Assert.Equal(30, advanced.Min);
        Assert.Equal(31.5, advanced.Max);
    });

    [Fact]
    public void Unloaded_chart_rebuilds_from_current_history_when_loaded_again() => ReviewTimelineNavigatorTests.OnSta(() =>
    {
        var model = new RbpReviewAnalysisViewModel();
        model.Apply(RbpReviewPresentationTests.Ready(5), 5);
        var history = model.ChannelHistories[0];
        var chart = new RbpChart { IsStackedHistory = true, Bands = history.Bands, Windows = history.Windows };
        Load(chart);
        var surface = Assert.IsType<SciChartSurface>(chart.Content);
        var series = Assert.IsType<XyDataSeries<double, double>>(surface.RenderableSeries[0].DataSeries);

        chart.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        model.Apply(RbpReviewPresentationTests.Ready(6), 6);

        Assert.Equal(new[] { 5d }, series.XValues);
        Load(chart);

        var reloaded = Assert.IsType<XyDataSeries<double, double>>(surface.RenderableSeries[0].DataSeries);
        Assert.Equal(new[] { 5d, 6d }, reloaded.XValues);
    });

    [Theory]
    [InlineData(640)]
    [InlineData(900)]
    public void Panel_realizes_only_visible_channel_charts_and_recycles_on_scroll(double height) =>
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var model = new RbpReviewAnalysisViewModel();
            model.Apply(RbpReviewPresentationTests.Ready(5), 5);
            var panel = new RbpReviewAnalysisPanel
            {
                DataContext = new { RbpAnalysis = model, AnalysisPreparationText = "RBP 准备失败：记录不可用。" },
            };
            panel.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/BrainPlatform.Desktop;component/Shared/Styles/ControlStyles.xaml", UriKind.Relative),
            });
            Arrange(panel, height);
            LoadCharts(panel);
            var list = Assert.IsType<ListBox>(panel.FindName("ChannelHistoryList"));
            var shell = Assert.Single(Descendants<AnalysisPanelShell>(panel));
            Assert.Empty(shell.Title);
            Assert.Empty(shell.StatusText);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Grid>(shell.Template.FindName("PART_StatusRow", shell)).Visibility);
            Assert.True(list.ActualHeight > 0);
            Assert.InRange(Descendants<RbpChart>(list).Count(), 1, 8);
            Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(20));

            list.ScrollIntoView(model.ChannelHistories[20]);
            Arrange(panel, height);
            LoadCharts(panel);

            Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(20));
            Assert.InRange(Descendants<RbpChart>(list).Count(), 1, 8);
            model.Apply(RbpReviewPresentationTests.Ready(6), 6);
            Assert.All(Descendants<RbpChart>(list), chart =>
            {
                var surface = Assert.IsType<SciChartSurface>(chart.Content);
                Assert.All(surface.XAxes.Concat(surface.YAxes), axis =>
                {
                    Assert.Empty(axis.AxisTitle);
                    Assert.True(axis.DrawLabels);
                    Assert.True(axis.DrawMajorTicks);
                });
                Assert.Equal("0%", Assert.Single(surface.YAxes).TextFormatting);
                var history = Assert.IsType<RbpChannelHistory>(chart.DataContext);
                Assert.Same(history.Windows, chart.Windows);
                Assert.Equal(2, chart.Windows!.Count);
            });
            model.Apply(null, 6);
            Assert.Equal("RBP 准备失败：记录不可用。", shell.StatusText);
            Assert.Equal(Visibility.Visible,
                Assert.IsType<Grid>(shell.Template.FindName("PART_StatusRow", shell)).Visibility);
        });

    private static void Arrange(FrameworkElement element, double height)
    {
        element.Measure(new Size(620, height));
        element.Arrange(new Rect(0, 0, 620, height));
        element.UpdateLayout();
    }

    private static void Load(RbpChart chart) =>
        chart.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

    private static void LoadCharts(DependencyObject root)
    {
        foreach (var chart in Descendants<RbpChart>(root)) Load(chart);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
