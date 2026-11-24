using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BrainPlatform.Desktop.Shared.Controls;
using BrainPlatform.Desktop.Shared.Time;
using BrainPlatform.Desktop.Shared.Waveform;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class RecordingTimePresentationTests
{
    [Fact]
    public void Time_axis_inherits_context_and_preserves_values_ranges_and_frequency_formatting()
    {
        OnSta(() =>
        {
            var context = new RecordingTimeDisplayContext(new RecordingTimeDisplaySettings(), DateTimeOffset.UtcNow);
            var parent = new Grid();
            RecordingTimeAxisPresentation.SetContext(parent, context);
            var surface = new SciChartSurface();
            var axis = new NumericAxis { AxisTitle = "记录时间（s）", VisibleRange = new DoubleRange(17, 20) };
            var originalLabels = axis.LabelProvider;
            surface.XAxes.Add(axis);
            surface.YAxes.Add(new NumericAxis());
            var values = new XyDataSeries<double, double>();
            values.Append(17, 1);
            values.Append(18, double.NaN);
            surface.RenderableSeries.Add(new FastLineRenderableSeries { DataSeries = values });
            var chart = new UserControl { Content = surface };
            RecordingTimeAxisPresentation.SetAxis(chart, axis);
            parent.Children.Add(chart);
            Assert.Equal("17", axis.LabelProvider.FormatLabel(17d));

            context.IsClockTime = true;
            Assert.Equal(context.FormatAxis(17), axis.LabelProvider.FormatLabel(17d));
            Assert.Equal(context.FormatCursor(17), axis.LabelProvider.FormatCursorLabel(17d));
            Assert.Equal("采集时间", axis.AxisTitle);
            Assert.Equal(17d, Convert.ToDouble(axis.VisibleRange.Min));
            Assert.Equal(20d, Convert.ToDouble(axis.VisibleRange.Max));
            Assert.Equal(new[] { 17d, 18d }, values.XValues);
            Assert.Equal(1, values.YValues[0]);
            Assert.True(double.IsNaN(values.YValues[1]));

            RecordingTimeAxisPresentation.SetAxis(chart, null);
            axis.AxisTitle = "频率（Hz）";
            context.IsRecordTime = true;
            context.IsClockTime = true;
            Assert.Same(originalLabels, axis.LabelProvider);
            Assert.Equal("频率（Hz）", axis.AxisTitle);
        });
    }

    [Theory]
    [InlineData(180, false)]
    [InlineData(220, true)]
    public void Selector_binds_both_modes_and_fits_its_layout(double width, bool hasOrigin)
    {
        OnSta(() =>
        {
            var context = new RecordingTimeDisplayContext(new RecordingTimeDisplaySettings(),
                hasOrigin ? DateTimeOffset.UtcNow : null);
            var control = new RecordingTimeSelector { DataContext = context };
            control.Measure(new Size(width, 100));
            control.Arrange(new Rect(0, 0, width, control.DesiredSize.Height));
            control.UpdateLayout();
            var segments = FindChildren<RadioButton>(control).ToArray();
            Assert.Equal(2, segments.Length);
            Assert.True(segments[0].IsChecked);
            Assert.Equal(hasOrigin, segments[1].IsEnabled);
            Assert.True(segments.Sum(segment => segment.ActualWidth) + 6 <= width);
            foreach (var segment in segments)
                Assert.True(segment.DesiredSize.Height <= control.ActualHeight);
            if (hasOrigin)
            {
                segments[1].IsChecked = true;
                Assert.True(context.IsClockTime);
                Assert.False(segments[0].IsChecked);
            }
            control.UpdateLayout();
            control.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            SavePreview(control, $"time-selector-{width}-{hasOrigin}.png");
        });
    }

    [Fact]
    public void Algorithm_display_uses_typed_origin_without_changing_analysis_input_or_run()
    {
        var client = new NoRequestsClient();
        var settings = new RecordingTimeDisplaySettings();
        var catalog = new AlgorithmListViewModel(client, new OperationNotificationCenter(), timeDisplaySettings: settings);
        var start = new DateTimeOffset(2026, 10, 10, 1, 0, 0, 387, TimeSpan.Zero);
        catalog.SelectedProjectRecording = new ProjectRecordingRow("recording", "recording", "不可解析的显示文本",
            "100s", "500Hz", "1", "已中止", "unused", start);
        catalog.StartSecondsText = "38";
        catalog.EndSecondsText = "45";
        typeof(AlgorithmListViewModel).GetProperty(nameof(catalog.DynamicPreviewCursorSeconds))!.SetValue(catalog, 17d);
        var run = new AnalysisRunResponse("run", "recording", "official_algorithm", "completed", "v1",
            new TimeRange(38, 45), new TimeRange(38, 45), null, null, null, start, start);
        typeof(AlgorithmListViewModel).GetProperty(nameof(catalog.LastRun))!.SetValue(catalog, run);
        var context = new AnalysisContextViewModel(catalog);

        settings.Mode = RecordingTimeDisplayMode.Clock;

        Assert.Equal(start, context.TimeDisplay.RecordingStartUtc);
        Assert.Contains(start.AddSeconds(38).ToLocalTime().ToString("HH:mm:ss.fff"), context.RequestedRangeText);
        Assert.Equal("38", context.StartSeconds);
        Assert.Equal("45", context.EndSeconds);
        Assert.Equal(new TimeRange(38, 45), AlgorithmRunConfiguration.ParseStaticRangeOrThrow(context.StartSeconds, context.EndSeconds, 100));
        Assert.Equal(17, context.DynamicPreviewCursorSeconds);
        Assert.Contains("17 s", context.DynamicPreviewCursorText);
        Assert.Same(run, catalog.LastRun);
        Assert.Equal(0, client.Requests);
    }

    private static IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in FindChildren<T>(child)) yield return nested;
        }
    }

    private static void SavePreview(FrameworkElement control, string name)
    {
        var directory = Environment.GetEnvironmentVariable("BRAIN_TIME_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(control.ActualWidth),
            (int)Math.Ceiling(control.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(control);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name));
        encoder.Save(stream);
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class NoRequestsClient : IAlgorithmClient
    {
        public int Requests { get; private set; }
        private T Fail<T>() { Requests++; throw new InvalidOperationException("Display changes must not call the backend."); }
        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) => Fail<Task<IReadOnlyList<AlgorithmCatalogItem>>>();
        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) => Fail<Task<AnalysisRunResponse>>();
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken) => Fail<Task<AnalysisRunResponse>>();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) => Fail<Task<IReadOnlyList<RunArtifact>>>();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) => Fail<Task<StructuredPreviewResponse>>();
    }
}
