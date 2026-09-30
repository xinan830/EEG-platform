using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Stft;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Charting.Model.DataSeries;

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

                var surface = Assert.IsType<SciChartSurface>(FindVisualChild<SciChartSurface>(control));
                var heatmap = Assert.IsType<FastUniformHeatmapRenderableSeries>(surface.RenderableSeries[0]);
                Assert.NotNull(heatmap.DataSeries);
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
        [], new Dictionary<string, int>(), null, "spectrogram-v2", "stft-runtime-v1");

    private static StructuredPreviewResponse CreatePreview() => new(
        "run", new RunArtifact("artifact", "run", "stft", "stft.npz", "application/x-npz", 1, "hash", null, new Dictionary<string, int[]>(), DateTimeOffset.UtcNow),
        Json("{\"kind\":\"time_frequency\"}"), ["O1"], new TimeRange(0, 4), new TimeRange(0, 4),
        new Dictionary<string, JsonElement> { ["time_center_s"] = Json("[1,2]"), ["frequency_hz"] = Json("[1,2]") },
        new Dictionary<string, JsonElement> { ["time_center_s"] = Json("{\"unit\":\"s\"}"), ["frequency_hz"] = Json("{\"unit\":\"Hz\"}") },
        new Dictionary<string, JsonElement> { ["power_db"] = Json("[[1,2],[3,4]]") },
        new Dictionary<string, JsonElement> { ["power_db"] = Json("{\"unit\":\"dB re 1 uV^2/Hz\"}") },
        [], new Dictionary<string, int>(), null, "spectrogram-v2", "stft-runtime-v1");

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
