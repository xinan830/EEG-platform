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
    public void New_window_appends_to_existing_chart_series() => ReviewTimelineNavigatorTests.OnSta(() =>
    {
        var model = new RbpReviewAnalysisViewModel();
        model.Apply(RbpReviewPresentationTests.Ready(5), 5);
        var history = model.ChannelHistories[0];
        var chart = new RbpChart { IsStackedHistory = true, Bands = history.Bands, Windows = history.Windows };
        var surface = Assert.IsType<SciChartSurface>(chart.Content);
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
        var xAxis = Assert.Single(surface.XAxes);
        var initial = Assert.IsType<SciChart.Data.Model.DoubleRange>(xAxis.VisibleRange);
        Assert.Equal(30, initial.Min);
        Assert.Equal(31, initial.Max);

        model.Apply(RbpReviewPresentationTests.Ready(31), 31);

        var advanced = Assert.IsType<SciChart.Data.Model.DoubleRange>(xAxis.VisibleRange);
        Assert.Equal(30, advanced.Min);
        Assert.Equal(31.5, advanced.Max);
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
