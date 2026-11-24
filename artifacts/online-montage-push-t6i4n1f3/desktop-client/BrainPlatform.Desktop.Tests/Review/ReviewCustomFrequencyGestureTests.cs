using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Modules.Review.Views;
using BrainPlatform.Desktop.Tests.Shared;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Annotations;

namespace BrainPlatform.Desktop.Tests.Review;

[Collection("WPF application resources")]
public sealed class ReviewCustomFrequencyGestureTests(WpfApplicationFixture fixture)
{
    [Fact]
    public void One_press_drags_point_line_label_handle_or_plot_and_reuses_data() => fixture.Run(() =>
    {
        var model = Model();
        var chart = new ReviewCustomFrequencyChart { Model = model };
        var window = Host(chart);
        try
        {
            var surface = Surface(chart);
            var line = surface.Annotations.OfType<VerticalLineAnnotation>().First();
            var curve = model.PsdCurve;
            var data = surface.RenderableSeries.Single().DataSeries;
            var abovePlot = new Point(Position(chart, 10).X, -10);
            Assert.False((bool)Invoke(chart, "BeginSelectionDrag", abovePlot, surface)!);
            Assert.True((bool)Invoke(chart, "BeginSelectionDrag", abovePlot, line.AnnotationLabels[0])!);
            End(chart);
            foreach (var source in new DependencyObject[] { line, line.AnnotationLabels[0], line.AnnotationLabels[1], surface })
            {
                model.SelectedFrequencyHz = 10;
                Assert.False(line.IsEditable);
                Assert.True(Begin(chart, 10, source));
                Assert.True(surface.IsMouseCaptured);
                Move(chart, 12.6);
                Assert.Equal(13, model.ActualFrequencyHz);
                Assert.Equal(13d, line.X1);
                Assert.Equal(13d, model.Rows[0].PsdDensityUv2PerHz);
                End(chart);
                Assert.False(surface.IsMouseCaptured);
                Move(chart, 20);
                Assert.Equal(13, model.ActualFrequencyHz);
            }
            Assert.True(Begin(chart, 17.2, surface));
            Assert.Equal(17, model.ActualFrequencyHz);
            Move(chart, 19.6);
            Assert.Equal(20, model.ActualFrequencyHz);
            Assert.Same(curve, model.PsdCurve);
            Assert.Same(data, surface.RenderableSeries.Single().DataSeries);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Filled_range_translates_at_fixed_width_snaps_and_publishes_complete_pairs() => fixture.Run(() =>
    {
        var model = Model();
        model.Mode = ReviewCustomFrequencyMode.Range;
        model.SetRange(8, 13);
        var chart = new ReviewCustomFrequencyChart { Model = model };
        var window = Host(chart);
        try
        {
            var curve = model.PsdCurve;
            var notifications = new List<(double Low, double High)>();
            model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(model.RangeLowHz) or nameof(model.RangeHighHz))
                    notifications.Add((model.RangeLowHz, model.RangeHighHz));
            };
            Assert.False(Begin(chart, 20));
            Assert.True(Begin(chart, 10.5));
            Move(chart, 11.6);
            Assert.Equal((9d, 14d), (model.RangeLowHz, model.RangeHighHz));
            Assert.Equal(new[] { (9d, 14d), (9d, 14d) }, notifications);
            Move(chart, 11.7);
            Assert.Equal(2, notifications.Count);
            Move(chart, 100);
            Assert.Equal((45d, 50d), (model.RangeLowHz, model.RangeHighHz));
            Move(chart, -100);
            Assert.Equal((1d, 6d), (model.RangeLowHz, model.RangeHighHz));
            Move(chart, 10.5);
            Assert.Equal((8d, 13d), (model.RangeLowHz, model.RangeHighHz));
            Assert.Same(curve, model.PsdCurve);
            Assert.All(notifications, pair => Assert.Equal(5, pair.High - pair.Low));
            var range = Surface(chart).Annotations.OfType<BoxAnnotation>().Single();
            Assert.Equal(8d, range.X1);
            Assert.Equal(13d, range.X2);
            End(chart);
            Assert.True(Begin(chart, 8));
            Move(chart, 6.6);
            Assert.Equal((7d, 13d), (model.RangeLowHz, model.RangeHighHz));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Nonuniform_grid_keeps_Hz_width_and_snaps_only_to_supported_pairs() => fixture.Run(() =>
    {
        var model = Model([1, 2, 3, 6, 7, 10]);
        model.Mode = ReviewCustomFrequencyMode.Range;
        model.SetRange(1, 2);
        var chart = new ReviewCustomFrequencyChart { Model = model };
        var window = Host(chart);
        try
        {
            Assert.True(Begin(chart, 1.5));
            Move(chart, 6.5);
            Assert.Equal((6d, 7d), (model.RangeLowHz, model.RangeHighHz));
            Move(chart, 50);
            Assert.Equal((6d, 7d), (model.RangeLowHz, model.RangeHighHz));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Capture_loss_mode_bins_replacement_clear_and_unload_cancel_drag() => fixture.Run(() =>
    {
        var model = Model();
        var chart = new ReviewCustomFrequencyChart { Model = model };
        var window = Host(chart);
        try
        {
            Assert.True(Begin(chart, 10));
            Surface(chart).ReleaseMouseCapture();
            Move(chart, 20);
            Assert.Equal(10, model.ActualFrequencyHz);
            Assert.True(Begin(chart, 10));
            model.Mode = ReviewCustomFrequencyMode.Range;
            Assert.False(Surface(chart).IsMouseCaptured);
            model.SetRange(8, 13);
            Assert.True(Begin(chart, 10));
            ApplyBins(model, Enumerable.Range(1, 50).Select(n => (double)n).ToArray());
            Assert.False(Surface(chart).IsMouseCaptured);
            Move(chart, 20);
            Assert.Equal((8d, 13d), (model.RangeLowHz, model.RangeHighHz));
            Assert.True(Begin(chart, 10));
            model.Clear("无数据");
            Assert.False(Surface(chart).IsMouseCaptured);
            Assert.False(Begin(chart, 10));
            chart.Model = Model();
            Assert.True(Begin(chart, 10));
            var replacement = Model();
            chart.Model = replacement;
            Assert.False(Surface(chart).IsMouseCaptured);
            Assert.True(Begin(chart, 10));
            window.Content = null;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Assert.False(Surface(chart).IsMouseCaptured);
            Move(chart, 20);
            Assert.Equal(10, replacement.ActualFrequencyHz);
        }
        finally { window.Close(); }
    });

    private static ReviewCustomFrequencyInspectionViewModel Model(double[]? axis = null)
    {
        var model = new ReviewCustomFrequencyInspectionViewModel();
        model.InitializeChannels(["Fp1-AVG"]);
        ApplyBins(model, axis ?? Enumerable.Range(1, 50).Select(n => (double)n).ToArray());
        return model;
    }

    private static void ApplyBins(ReviewCustomFrequencyInspectionViewModel model, double[] axis)
    {
        var psd = ReviewSpectralFixture.Window("psd", channels: ["Fp1-AVG"]);
        var rbp = ReviewSpectralFixture.Window("rbp", channels: ["Fp1-AVG"]);
        CustomFrequencyProvenance Source(WindowPreviewResponse window) => new(window.Window!.WindowId,
            "fixture-v1", "impl", window.BackendIdentity!.Value.GetProperty("configuration_sha256").GetString()!, "policy");
        var intervals = axis.Select((f, i) => new CustomFrequencyBinInterval(
            i == 0 ? f : (axis[i - 1] + f) / 2,
            i == axis.Length - 1 ? f : (f + axis[i + 1]) / 2)).ToArray();
        var metrics = axis.Select(f => (double?)f).ToArray();
        model.ApplyBins(psd, rbp, new CustomFrequencyBinsResponse("bins", "registered", 5000, 10000,
            axis, intervals, [new("Fp1-AVG", "clean", [], "clean", [], 100, metrics, metrics, metrics, metrics)],
            Source(psd), Source(rbp)));
    }

    private static SciChartSurface Surface(ReviewCustomFrequencyChart chart) => (SciChartSurface)((Grid)chart.Content).Children[0];
    private static Point Position(ReviewCustomFrequencyChart chart, double hz) =>
        new(Surface(chart).XAxes[0].GetCurrentCoordinateCalculator().GetCoordinate(hz), 100);
    private static bool Begin(ReviewCustomFrequencyChart chart, double hz, DependencyObject? source = null) =>
        (bool)Invoke(chart, "BeginSelectionDrag", Position(chart, hz), source)!;
    private static void Move(ReviewCustomFrequencyChart chart, double hz) => Invoke(chart, "MoveSelectionDrag", Position(chart, hz));
    private static void End(ReviewCustomFrequencyChart chart) => Invoke(chart, "EndSelectionDrag");
    private static object? Invoke(ReviewCustomFrequencyChart chart, string name, params object?[] arguments) =>
        typeof(ReviewCustomFrequencyChart).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(chart, arguments);
    private static Window Host(FrameworkElement chart)
    {
        var window = new Window { Width = 1000, Height = 620, Content = chart, ShowInTaskbar = false };
        window.Show();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        return window;
    }
}
