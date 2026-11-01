using System.Reflection;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Stft;
using BrainPlatform.Desktop.Modules.Review.Views;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Shared.Controls.Analysis;
using BrainPlatform.Desktop.Shared.Time;
using BrainPlatform.Desktop.Shared.Waveform;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class StftReviewChartTests
{
    [Fact]
    public void Channel_selector_shares_header_row_with_dock_and_close_without_evidence_expander() =>
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var time = new RecordingTimeDisplayContext(new RecordingTimeDisplaySettings(), DateTimeOffset.UtcNow);
            var panel = new StftReviewAnalysisPanel { DataContext = new { TimeDisplay = time } };
            panel.Measure(new Size(600, 300));
            panel.Arrange(new Rect(0, 0, 600, 300));
            panel.UpdateLayout();

            var selector = Find<ComboBox>(panel);
            var shell = Find<AnalysisPanelShell>(panel)!;
            var dock = Assert.IsType<Button>(shell.Template.FindName("PART_DockButton", shell));
            var close = Assert.Single(FindAll<Button>(shell),
                button => AutomationProperties.GetName(button) == "关闭分析面板");
            Assert.NotNull(selector);
            var selectorCenter = selector!.TranslatePoint(new Point(0, selector.ActualHeight / 2), panel).Y;
            Assert.All(new[] { dock, close }, button => Assert.Equal(selectorCenter,
                button.TranslatePoint(new Point(0, button.ActualHeight / 2), panel).Y, 1));
            Assert.Null(Find<Expander>(panel));
        });

    [Fact]
    public async Task History_appends_only_new_times_resets_on_backward_seek_and_inherits_time_display()
    {
        var preview = await StftReviewPresentationTests.Ready();
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var time = new RecordingTimeDisplayContext(new RecordingTimeDisplaySettings(), DateTimeOffset.UtcNow);
            var model = new StftReviewAnalysisViewModel();
            var panel = new StftReviewAnalysisPanel { DataContext = new { TimeDisplay = time, StftAnalysis = model } };
            panel.Measure(new Size(600, 300));
            panel.Arrange(new Rect(0, 0, 600, 300));
            panel.UpdateLayout();
            var chart = Find<StftHeatmap>(panel)!;
            Assert.NotNull(chart);
            Assert.True(chart.RetainHistory);
            Assert.Same(time, RecordingTimeAxisPresentation.GetContext(chart));
            model.Apply(preview);
            var surface = Find<SciChartSurface>(chart)!;
            var old = surface.RenderableSeries.OfType<FastUniformHeatmapRenderableSeries>().ToArray();
            Assert.NotEmpty(old);
            model.Apply(preview with { WindowStartSeconds = 6, WindowEndSeconds = 11 });
            Assert.All(old, series => Assert.Contains(series, surface.RenderableSeries));
            Assert.Equal(6, surface.RenderableSeries.Sum(series => series.DataSeries!.Count));
            model.Apply(preview with { WindowStartSeconds = 6, WindowEndSeconds = 11 });
            Assert.Equal(6, surface.RenderableSeries.Sum(series => series.DataSeries!.Count));
            model.Apply(preview with { WindowStartSeconds = 0, WindowEndSeconds = 5 });
            Assert.All(old, series => Assert.Null(series.DataSeries));
            Assert.All(surface.RenderableSeries, series => Assert.DoesNotContain(series, old));
            var axis = (NumericAxis)typeof(StftHeatmap).GetField("xAxis", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart)!;
            time.IsClockTime = true;
            Assert.Equal(time.FormatAxis(2), axis.LabelProvider.FormatLabel(2d));
            Assert.Equal(4, surface.RenderableSeries.Sum(series => series.DataSeries!.Count));
            model.Apply(null);
            Assert.Empty(surface.RenderableSeries);
        });
    }

    [Fact]
    public async Task History_preserves_forward_gaps_and_clears_on_rejection()
    {
        var preview = await StftReviewPresentationTests.Ready();
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var model = new StftReviewAnalysisViewModel();
            var chart = new StftHeatmap();
            chart.Measure(new Size(600, 300));
            chart.Arrange(new Rect(0, 0, 600, 300));
            chart.UpdateLayout();
            chart.SetBinding(StftHeatmap.PreviewProperty, new System.Windows.Data.Binding(nameof(model.Preview)) { Source = model });
            var surface = Find<SciChartSurface>(chart)!;
            model.Apply(preview);
            model.Apply(preview with { WindowStartSeconds = 15, WindowEndSeconds = 20 });
            Assert.Equal(2, surface.RenderableSeries.Count);
            Assert.Equal(8, surface.RenderableSeries.Sum(series => series.DataSeries!.Count));
            model.Apply(preview with { Status = "rejected" });
            Assert.Empty(surface.RenderableSeries);
            Assert.False(model.HasResult);
            model.Apply(preview);
            Assert.Equal(4, surface.RenderableSeries.Sum(series => series.DataSeries!.Count));
        });
    }

    private static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (Find<T>(child) is { } found) return found;
        }
        return null;
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindAll<T>(child)) yield return descendant;
        }
    }
}
