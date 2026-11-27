using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Stft;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Model.DataSeries.Heatmap2DArrayDataSeries;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class StftHeatmapTests
{
    [Fact]
    public void Preview_CreatesSciChartHeatmapAndClearingItRemovesOldData()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new StftHeatmap();
                control.Measure(new Size(500, 300));
                control.Arrange(new Rect(0, 0, 500, 300));
                control.Preview = StftPreview.Parse(CreatePreview());

                var xAxis = Assert.IsType<NumericAxis>(typeof(StftHeatmap)
                    .GetField("xAxis", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control));
                var yAxis = Assert.IsType<NumericAxis>(typeof(StftHeatmap)
                    .GetField("yAxis", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control));
                Assert.Equal("", xAxis.AxisTitle);
                Assert.Equal(42, xAxis.Height);
                Assert.False(xAxis.DrawMajorGridLines);
                Assert.Equal("", yAxis.AxisTitle);
                Assert.Equal(32, yAxis.Width);

                var surface = Assert.IsType<SciChartSurface>(FindVisualChild<SciChartSurface>(control));
                var heatmap = Assert.IsType<FastNonUniformHeatmapRenderableSeries>(surface.RenderableSeries[0]);
                Assert.NotNull(heatmap.DataSeries);
                Assert.False(heatmap.UseLinearTextureFiltering);
                Assert.Equal(4, heatmap.DataSeries!.Count);

                control.Preview = null;
                Assert.Null(heatmap.DataSeries);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void Preview_AppendKeepsExistingSegmentAndAddsOnlyNewColumns()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new StftHeatmap();
                control.Measure(new Size(500, 300));
                control.Arrange(new Rect(0, 0, 500, 300));
                var first = StftPreview.Parse(CreatePreview());
                control.Preview = first;

                var surface = Assert.IsType<SciChartSurface>(FindVisualChild<SciChartSurface>(control));
                var oldSegment = Assert.IsType<FastNonUniformHeatmapRenderableSeries>(surface.RenderableSeries[0]);
                var oldDataSeries = oldSegment.DataSeries;
                var initialSeriesCount = surface.RenderableSeries.Count;
                var appended = StftPreview.Append(first, StftPreview.Parse(CreatePreviewAt("[3,4]", "[[5,6],[7,8]]")));
                control.Preview = appended;

                Assert.True(surface.RenderableSeries.Count > initialSeriesCount);
                Assert.Same(oldSegment, surface.RenderableSeries[0]);
                Assert.Same(oldDataSeries, oldSegment.DataSeries);
                Assert.Equal(4, oldSegment.DataSeries!.Count);
                var newSegments = surface.RenderableSeries
                    .OfType<FastNonUniformHeatmapRenderableSeries>()
                    .Where(segment => !ReferenceEquals(segment, oldSegment))
                    .ToArray();
                Assert.NotEmpty(newSegments);
                Assert.All(newSegments, segment => Assert.False(segment.UseLinearTextureFiltering));
                var appendedData = Assert.IsType<NonUniformHeatmapDataSeries<double, double, double>>(Assert.Single(newSegments).DataSeries);
                var previousData = Assert.IsType<NonUniformHeatmapDataSeries<double, double, double>>(oldSegment.DataSeries);
                Assert.Equal(6, appendedData.Count);
                Assert.Equal(1.5, appendedData.XMin);
                Assert.Equal(4.5, appendedData.XMax);
                Assert.True((double)appendedData.XMin < (double)previousData.XMax);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void Preview_LateWindowFillsPreviouslySkippedTimeColumns()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new StftHeatmap();
                control.Measure(new Size(500, 300));
                control.Arrange(new Rect(0, 0, 500, 300));
                var first = StftPreview.Parse(CreatePreview());
                var future = StftPreview.Parse(CreatePreviewAt("[5,6]", "[[5,6],[7,8]]"));
                control.Preview = StftPreview.Append(first, future);

                var late = StftPreview.Parse(CreatePreviewAt("[4]", "[[9,10]]"));
                control.Preview = StftPreview.Append(control.Preview!, late);

                var surface = Assert.IsType<SciChartSurface>(FindVisualChild<SciChartSurface>(control));
                var segments = surface.RenderableSeries.OfType<FastNonUniformHeatmapRenderableSeries>().ToArray();
                Assert.Equal(3, segments.Length);
                Assert.Equal(10, segments.Sum(segment => segment.DataSeries!.Count));
                Assert.Equal(6d, typeof(StftHeatmap)
                    .GetField("renderedThroughSeconds", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(control));
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void LivePreview_StartsRenderingAtTheAlgorithmActivationTime()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new StftHeatmap { VisibleStartSeconds = 2.2 };
                control.Measure(new Size(500, 300));
                control.Arrange(new Rect(0, 0, 500, 300));
                control.Preview = StftPreview.Parse(CreatePreviewAt("[3,4]", "[[5,6],[7,8]]"));

                var surface = Assert.IsType<SciChartSurface>(FindVisualChild<SciChartSurface>(control));
                var axis = Assert.IsType<NumericAxis>(typeof(StftHeatmap)
                    .GetField("xAxis", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control));
                var range = Assert.IsType<SciChart.Data.Model.DoubleRange>(axis.VisibleRange);
                var heatmap = Assert.IsType<FastNonUniformHeatmapRenderableSeries>(Assert.Single(surface.RenderableSeries));
                var dataSeries = Assert.IsType<NonUniformHeatmapDataSeries<double, double, double>>(heatmap.DataSeries);

                Assert.Equal(2.2, range.Min);
                Assert.Equal(4.5, range.Max);
                Assert.Equal(2.2, dataSeries.XMin);
                Assert.Equal(4.5, dataSeries.XMax);
                Assert.Equal(1.3 / 2.3, dataSeries.XOffsets[0], 10);
                Assert.Equal(1.0 / 2.3, dataSeries.XOffsets[1], 10);
                Assert.Equal(2, dataSeries.ArrayWidth);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void SliceChart_UsesBackendMatrixRowsAndColumnsWithoutFillingMissingValues()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new StftSliceChart { Preview = StftPreview.Parse(CreatePreviewWithMissingCell()) };
                var surface = Assert.IsType<SciChartSurface>(control.Content);
                var series = Assert.IsType<FastLineRenderableSeries>(surface.RenderableSeries[0]);
                var points = Assert.IsType<XyDataSeries<double, double>>(series.DataSeries);

                control.SelectedIndex = 1;
                Assert.Equal(3, points.Count);
                Assert.Equal(4, points.YValues[0]);
                Assert.True(double.IsNaN(points.YValues[1]));
                Assert.Equal(6, points.YValues[2]);

                control.IsSpectrum = false;
                control.SelectedIndex = 0;
                Assert.Equal(2, points.Count);
                Assert.Equal(1, points.YValues[0]);
                Assert.Equal(4, points.YValues[1]);

                control.Preview = null;
                Assert.Equal(0, points.Count);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static StructuredPreviewResponse CreatePreviewWithMissingCell() => new(
        "run", new RunArtifact("artifact", "run", "stft", "stft.npz", "application/x-npz", 1, "hash", null, new Dictionary<string, int[]>(), DateTimeOffset.UtcNow),
        Json("{\"kind\":\"time_frequency\"}"), ["O1"], new TimeRange(0, 4), new TimeRange(0, 4),
        new Dictionary<string, JsonElement> { ["time_center_s"] = Json("[1,2]"), ["frequency_hz"] = Json("[1,2,3]") },
        new Dictionary<string, JsonElement> { ["time_center_s"] = Json("{\"unit\":\"s\"}"), ["frequency_hz"] = Json("{\"unit\":\"Hz\"}") },
        new Dictionary<string, JsonElement> { ["power_db"] = Json("[[1,2,3],[4,null,6]]") },
        new Dictionary<string, JsonElement> { ["power_db"] = Json("{\"unit\":\"dB re 1 uV^2/Hz\"}") },
        [], new Dictionary<string, int>(), null, "spectrogram-v3-bounded", "stft-runtime-v1");

    private static StructuredPreviewResponse CreatePreview() => new(
        "run", new RunArtifact("artifact", "run", "stft", "stft.npz", "application/x-npz", 1, "hash", null, new Dictionary<string, int[]>(), DateTimeOffset.UtcNow),
        Json("{\"kind\":\"time_frequency\"}"), ["O1"], new TimeRange(0, 4), new TimeRange(0, 4),
        new Dictionary<string, JsonElement> { ["time_center_s"] = Json("[1,2]"), ["frequency_hz"] = Json("[1,2]") },
        new Dictionary<string, JsonElement> { ["time_center_s"] = Json("{\"unit\":\"s\"}"), ["frequency_hz"] = Json("{\"unit\":\"Hz\"}") },
        new Dictionary<string, JsonElement> { ["power_db"] = Json("[[1,2],[3,4]]") },
        new Dictionary<string, JsonElement> { ["power_db"] = Json("{\"unit\":\"dB re 1 uV^2/Hz\"}") },
        [], new Dictionary<string, int>(), null, "spectrogram-v3-bounded", "stft-runtime-v1");

    private static StructuredPreviewResponse CreatePreviewAt(string times, string matrix) => new(
        "run", new RunArtifact("artifact", "run", "stft", "stft.npz", "application/x-npz", 1, "hash", null, new Dictionary<string, int[]>(), DateTimeOffset.UtcNow),
        Json("{\"kind\":\"time_frequency\"}"), ["O1"], new TimeRange(2, 6), new TimeRange(2, 6),
        new Dictionary<string, JsonElement> { ["time_center_s"] = Json(times), ["frequency_hz"] = Json("[1,2]") },
        new Dictionary<string, JsonElement> { ["time_center_s"] = Json("{\"unit\":\"s\"}"), ["frequency_hz"] = Json("{\"unit\":\"Hz\"}") },
        new Dictionary<string, JsonElement> { ["power_db"] = Json(matrix) },
        new Dictionary<string, JsonElement> { ["power_db"] = Json("{\"unit\":\"dB re 1 uV^2/Hz\"}") },
        [], new Dictionary<string, int>(), null, "spectrogram-v3-bounded", "stft-runtime-v1");

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } descendant) return descendant;
        }
        return null;
    }
}
