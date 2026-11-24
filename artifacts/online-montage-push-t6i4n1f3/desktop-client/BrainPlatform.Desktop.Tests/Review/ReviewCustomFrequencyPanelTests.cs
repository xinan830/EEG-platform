using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Modules.Review.Views;
using BrainPlatform.Desktop.Tests.Shared;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Annotations;

namespace BrainPlatform.Desktop.Tests.Review;

[Collection("WPF application resources")]
public sealed partial class ReviewCustomFrequencyPanelTests(WpfApplicationFixture fixture)
{
    [Fact]
    public void Rendered_panel_binds_numeric_table_selection_sorting_inputs_and_scrollbars() => fixture.Run(() =>
    {
        var model = Model(28);
        var panel = new ReviewCustomFrequencyInspectionPanel { DataContext = new PanelContext(model) };
        var window = Host(panel);
        try
        {
            var table = (DataGrid)panel.FindName("ChannelTable");
            var chart = (ReviewCustomFrequencyChart)panel.FindName("FrequencyChart");
            var surface = Descendants<SciChartSurface>(chart).Single();
            Assert.Same(model.Rows, table.ItemsSource);
            Assert.Same(model, chart.Model);
            Assert.Equal(28, table.Items.Count);
            Assert.Equal(model.SelectedChannel, table.SelectedValue);
            Assert.Equal(new[] { "Channel", "AmplitudeRmsUv", "AbsolutePowerUv2", "RelativePowerPercent", "PsdDensityUv2PerHz" },
                table.Columns.Cast<DataGridTextColumn>().Select(column => ((Binding)column.Binding).Path.Path));
            Assert.All(table.Columns.Cast<DataGridTextColumn>().Skip(1), column =>
            {
                var binding = (Binding)column.Binding;
                Assert.Equal("F2", binding.StringFormat);
                Assert.Equal("—", binding.TargetNullValue);
            });
            table.Items.SortDescriptions.Add(new SortDescription(nameof(ReviewCustomFrequencyRow.Channel), ListSortDirection.Descending));
            table.SelectedItem = table.Items[0];
            Drain(window);
            Assert.Equal(((ReviewCustomFrequencyRow)table.Items[0]).Channel, model.SelectedChannel);
            Assert.Equal("F01-AVG", model.Rows[0].Channel);
            Assert.Equal("F28-AVG", model.SelectedChannel);
            var series = Assert.IsType<XyDataSeries<double, double>>(surface.RenderableSeries.Single().DataSeries);
            Assert.Equal(28d, series.YValues[0]);

            var input = (TextBox)panel.FindName("FrequencyInput");
            input.Text = 2.6.ToString(CultureInfo.CurrentCulture);
            input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Drain(window);
            Assert.Equal(3, model.ActualFrequencyHz);
            var mode = (ComboBox)panel.FindName("ModeSelector");
            mode.SelectedValue = ReviewCustomFrequencyMode.Range;
            Drain(window);
            Assert.Equal(ReviewCustomFrequencyMode.Range, model.Mode);
            Assert.All(model.Rows, row => Assert.Null(row.AbsolutePowerUv2));
            var low = (TextBox)panel.FindName("RangeLowInput");
            low.Text = 1.1.ToString(CultureInfo.CurrentCulture);
            low.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.Equal(1, model.RangeLowHz);
            var scroll = Descendants<ScrollViewer>(table).First();
            Assert.True(scroll.ScrollableWidth > 0);
            Assert.True(scroll.ScrollableHeight > 0);
            table.ScrollIntoView(table.Items[^1], table.Columns[^1]);
            Drain(window);
            Assert.True(scroll.HorizontalOffset > 0);
            Assert.True(scroll.VerticalOffset > 0);
            model.Mode = ReviewCustomFrequencyMode.SingleFrequency;
            model.Clear("验证不可用状态");
            Drain(window);
            Assert.All(model.Rows, row => Assert.Null(row.PsdDensityUv2PerHz));
            Assert.All(surface.Annotations.OfType<VerticalLineAnnotation>(), line => Assert.True(line.IsHidden));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Hover_label_snaps_to_curve_bin_without_changing_selection() => fixture.Run(() =>
    {
        var model = Model(2);
        var chart = new ReviewCustomFrequencyChart { Model = model };
        var window = Host(chart);
        try
        {
            var surface = Descendants<SciChartSurface>(chart).Single();
            var cursor = Assert.IsType<SciChart.Charting.ChartModifiers.CursorModifier>(surface.ChartModifier);
            var axis = surface.XAxes.Single();
            Hover(surface, 2.6);
            Assert.Equal("3 Hz", cursor.XAxisInfo.CursorFormattedDataValue);
            Assert.Equal("3 Hz", axis.FormatCursorText(2.5));
            Assert.Equal("2 Hz", axis.FormatCursorText(2.4));
            Assert.Equal("50 Hz", axis.FormatCursorText(26.6));
            Assert.Equal(string.Empty, axis.FormatCursorText(double.NaN));
            Assert.Equal(3, model.SelectedFrequencyHz);
            model.SelectedChannel = model.Rows[1].Channel;
            Drain(window);
            Hover(surface, 2.6);
            Assert.Equal("3 Hz", cursor.XAxisInfo.CursorFormattedDataValue);
            model.Mode = ReviewCustomFrequencyMode.Range;
            var low = model.RangeLowHz;
            var high = model.RangeHighHz;
            Hover(surface, 1.8);
            Assert.Equal("2 Hz", cursor.XAxisInfo.CursorFormattedDataValue);
            Assert.Equal(low, model.RangeLowHz);
            Assert.Equal(high, model.RangeHighHz);
            model.Clear("无可用数据");
            Assert.False(cursor.IsEnabled);
            Assert.Equal(string.Empty, axis.FormatCursorText(2.6));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Annotation_drag_updates_selection_without_rebuilding_series_and_unload_detaches() => fixture.Run(() =>
    {
        var model = Model(2);
        var chart = new ReviewCustomFrequencyChart { Model = model };
        var window = Host(chart);
        try
        {
            var surface = Descendants<SciChartSurface>(chart).Single();
            var series = surface.RenderableSeries.Single();
            var data = Assert.IsType<XyDataSeries<double, double>>(series.DataSeries);
            var curve = model.PsdCurve;
            var lines = surface.Annotations.OfType<VerticalLineAnnotation>().ToArray();
            Assert.False(lines[0].IsHidden);
            Assert.True(lines[1].IsHidden && lines[2].IsHidden);
            Drag(chart, lines[0], 2.6);
            Assert.Equal(3d, model.ActualFrequencyHz);
            Assert.Equal(3d, lines[0].X1);
            Assert.Equal(3d, model.Rows[0].AbsolutePowerUv2);
            Assert.Same(curve, model.PsdCurve);
            Assert.Same(series, surface.RenderableSeries.Single());
            Assert.Same(data, series.DataSeries);

            model.Mode = ReviewCustomFrequencyMode.Range;
            Assert.True(lines[0].IsHidden);
            Assert.False(lines[1].IsHidden || lines[2].IsHidden);
            Drag(chart, lines[1], 1.1);
            Drag(chart, lines[2], 2.9);
            Assert.Equal(1, model.RangeLowHz);
            Assert.Equal(3, model.RangeHighHz);
            var range = surface.Annotations.OfType<BoxAnnotation>().Single();
            Assert.False(range.IsHidden);
            Assert.Equal(1d, range.X1);
            Assert.Equal(3d, range.X2);
            model.RangeLowHz = 4;
            Assert.True(range.IsHidden);
            var cursor = Assert.IsType<SciChart.Charting.ChartModifiers.CursorModifier>(surface.ChartModifier);
            Assert.Equal(SciChart.Charting.ChartModifiers.CursorSnappingMode.CrosshairToSeriesX, cursor.SnappingMode);
            Assert.False(cursor.UseInterpolation);
            Assert.True(SciChart.Charting.ChartModifiers.CursorModifier.GetSnapToSeries((DependencyObject)series));
            Assert.Contains("µV²/Hz", data.SeriesName);

            window.Content = null;
            Drain(window);
            model.SelectedChannel = model.Rows[1].Channel;
            Assert.Equal(1d, data.YValues[0]);
            window.Content = chart;
            Drain(window);
            Assert.Equal(2d, data.YValues[0]);
            Assert.Same(series, surface.RenderableSeries.Single());
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Frequency_label_layout_keeps_close_and_edge_targets_visible_after_snapping() => fixture.Run(() =>
    {
        var model = Model(2);
        model.Mode = ReviewCustomFrequencyMode.Range;
        model.RangeLowHz = 1;
        model.RangeHighHz = 3;
        var chart = new ReviewCustomFrequencyChart { Model = model };
        var window = Host(chart);
        try
        {
            var surface = Descendants<SciChartSurface>(chart).Single();
            var data = surface.RenderableSeries.Single().DataSeries;
            var lines = surface.Annotations.OfType<VerticalLineAnnotation>().ToArray();
            foreach (var width in new[] { 1000d, 620d })
            {
                window.Width = width;
                Drain(window);
                MeasureLabels();
                Drag(chart, lines[1], 1.1);
                Drag(chart, lines[2], 2.9);
                Drain(window);
                var previousRight = 0d;
                foreach (var line in lines.Where(line => !line.IsHidden))
                {
                    Assert.Equal(2, line.AnnotationLabels.Count);
                    var label = line.AnnotationLabels[0];
                    var handle = line.AnnotationLabels[1];
                    Assert.Equal(LabelPlacement.Top, label.LabelPlacement);
                    Assert.Equal(Brushes.White, label.Foreground);
                    Assert.True(((TranslateTransform)label.RenderTransform).Y + label.ActualHeight <= 0);
                    var bounds = LabelBounds(line);
                    Assert.True(bounds.Left >= previousRight - 1 && bounds.Right <= surface.ActualWidth + 1);
                    previousRight = bounds.Right;
                    Assert.Equal(LabelPlacement.Bottom, handle.LabelPlacement);
                    Assert.Equal(line.Stroke, handle.Background);
                    Assert.Equal(Brushes.White, handle.BorderBrush);
                    Assert.Equal(handle.Width, handle.Height);
                    Assert.Equal(new CornerRadius(handle.Width / 2), handle.CornerRadius);
                    Assert.True(handle.IsHitTestVisible);
                    Assert.Equal(0, ((TranslateTransform)handle.RenderTransform).X);
                }
            }
            model.Mode = ReviewCustomFrequencyMode.SingleFrequency;
            model.SelectedFrequencyHz = 50;
            MeasureLabels();
            Drag(chart, lines[0], 50);
            Drain(window);
            Assert.InRange(LabelBounds(lines[0]).Right, 0, surface.ActualWidth + 1);
            Assert.Equal(50d, lines[0].X1);
            Assert.Same(data, surface.RenderableSeries.Single().DataSeries);
            model.Clear("无数据");
            Assert.All(lines, line => Assert.True(line.IsHidden));

            void MeasureLabels()
            {
                // This host has no SciChart license; supply label sizes independently of its renderer.
                foreach (var line in lines)
                {
                    var label = line.AnnotationLabels[0];
                    label.Text = $"{Convert.ToDouble(line.X1):0.###} Hz";
                    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    label.Arrange(new Rect(label.DesiredSize));
                }
            }

            (double Left, double Right) LabelBounds(VerticalLineAnnotation line)
            {
                var label = line.AnnotationLabels[0];
                var center = ((FrameworkElement)surface.GridLinesPanel).TranslatePoint(new Point(
                    surface.XAxes[0].GetCurrentCoordinateCalculator().GetCoordinate(Convert.ToDouble(line.X1)), 0), surface).X;
                var left = center - label.ActualWidth / 2 + ((TranslateTransform)label.RenderTransform).X;
                return (left, left + label.ActualWidth);
            }
        }
        finally { window.Close(); }
    });

    private static void Hover(SciChartSurface surface, double frequency)
    {
        var point = new Point(surface.XAxes.Single().GetCurrentCoordinateCalculator().GetCoordinate(frequency), 100);
        surface.ChartModifier.GetType().GetMethod("HandleMasterMouseEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(surface.ChartModifier, [point]);
    }

    private static void Drag(ReviewCustomFrequencyChart chart, VerticalLineAnnotation line, double frequency)
    {
        typeof(ReviewCustomFrequencyChart).GetMethod("ApplyDraggedFrequency", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(chart, [line, frequency]);
    }

    private static ReviewCustomFrequencyInspectionViewModel Model(int count)
    {
        var channels = Enumerable.Range(1, count).Select(index => $"F{index:00}-AVG").ToArray();
        var model = new ReviewCustomFrequencyInspectionViewModel();
        model.InitializeChannels(channels);
        var psd = ReviewSpectralFixture.Window("psd", channels: channels);
        var rbp = ReviewSpectralFixture.Window("rbp", channels: channels);
        CustomFrequencyProvenance Source(WindowPreviewResponse window) => new(window.Window!.WindowId,
            "fixture-v1", "impl", window.BackendIdentity!.Value.GetProperty("configuration_sha256").GetString()!, "policy");
        model.ApplyBins(psd, rbp, new CustomFrequencyBinsResponse("bins", "registered", 5000, 10000,
            [1, 2, 3, 50], [new(1, 1.5), new(1.5, 2.5), new(2.5, 26.5), new(26.5, 50)],
            channels.Select((channel, index) => new CustomFrequencyBinChannel(channel, "clean", [], "clean", [],
                100, [index + 1, index + 2, index + 3, index + 4], [1, 2, 3, 4], [1, 2, 3, 4],
                [1, Math.Sqrt(2), Math.Sqrt(3), 2])).ToArray(),
            Source(psd), Source(rbp)));
        return model;
    }

    private static Window Host(FrameworkElement content)
    {
        var window = new Window { Width = 1000, Height = 620, Content = content, ShowInTaskbar = false };
        window.Show();
        Drain(window);
        return window;
    }

    private static void Drain(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed record PanelContext(ReviewCustomFrequencyInspectionViewModel CustomFrequencyInspection)
    {
        public ICommand? CloseAnalysisCommand => null;
        public string AnalysisPreparationText => "";
    }
}
