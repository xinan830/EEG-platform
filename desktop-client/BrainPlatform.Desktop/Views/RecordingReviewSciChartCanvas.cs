using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BrainPlatform.Desktop.Review;
using BrainPlatform.Desktop.ViewModels;
using SciChart.Charting;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Views;

public sealed class RecordingReviewSciChartCanvas : UserControl
{
    private const int MaximumPointsPerTrace = 2_000;
    private static readonly Color[] TraceColors =
    [
        Color.FromRgb(37, 99, 235), Color.FromRgb(2, 132, 199), Color.FromRgb(22, 163, 74),
        Color.FromRgb(245, 158, 11), Color.FromRgb(239, 68, 68), Color.FromRgb(147, 51, 234),
    ];
    private readonly SciChartSurface surface = new();
    private readonly NumericAxis xAxis = new();
    private readonly NumericAxis yAxis = new();
    private readonly Grid labels = new();
    private readonly TextBlock emptyMessage = new()
    {
        Text = "等待加载回溯数据",
        FontSize = 15,
        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly List<TraceSeries> traceSeries = [];
    private string[] activeLabels = [];

    public RecordingReviewSciChartCanvas()
    {
        ConfigureSurface();
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(labels, 0);
        Grid.SetColumn(surface, 1);
        Grid.SetColumn(emptyMessage, 1);
        layout.Children.Add(labels);
        layout.Children.Add(surface);
        layout.Children.Add(emptyMessage);
        Content = layout;
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Unsubscribe();
    }

    private void ConfigureSurface()
    {
        surface.Background = Brushes.White;
        VisualXcceleratorEngine.SetIsEnabled(surface, true);
        xAxis.AutoRange = AutoRange.Never;
        xAxis.VisibleRange = new DoubleRange(0, 10);
        xAxis.DrawMajorBands = false;
        xAxis.DrawMinorGridLines = false;
        xAxis.DrawMajorGridLines = true;
        xAxis.MajorDelta = 1;
        xAxis.MinorDelta = 0.5;
        xAxis.TextFormatting = "0";
        yAxis.AutoRange = AutoRange.Never;
        yAxis.VisibleRange = new DoubleRange(0, 1);
        yAxis.DrawLabels = false;
        yAxis.DrawMajorBands = false;
        yAxis.DrawMinorGridLines = false;
        yAxis.DrawMajorGridLines = false;
        surface.XAxes.Add(xAxis);
        surface.YAxes.Add(yAxis);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Unsubscribe(e.OldValue as RecordingReviewViewModel);
        Subscribe(e.NewValue as RecordingReviewViewModel);
        ApplyFrame((e.NewValue as RecordingReviewViewModel)?.CurrentFrame, e.NewValue as RecordingReviewViewModel);
    }

    private void Subscribe(RecordingReviewViewModel? viewModel)
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void Unsubscribe(RecordingReviewViewModel? viewModel = null)
    {
        (viewModel ?? DataContext as RecordingReviewViewModel)?.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RecordingReviewViewModel.CurrentFrame) or nameof(RecordingReviewViewModel.StatusText) or nameof(RecordingReviewViewModel.VisibleDurationSeconds))
        {
            ApplyFrame((sender as RecordingReviewViewModel)?.CurrentFrame, sender as RecordingReviewViewModel);
        }
    }

    private void ApplyFrame(RecordingReviewFrame? frame, RecordingReviewViewModel? viewModel)
    {
        if (frame is null)
        {
            emptyMessage.Text = viewModel?.StatusText ?? "等待加载回溯数据";
            emptyMessage.Visibility = Visibility.Visible;
            ClearSeries();
            return;
        }

        var names = frame.OutputChannelNames.ToArray();
        EnsureTraceSeries(names);
        var duration = Math.Max(0.1, viewModel?.VisibleDurationSeconds ?? 10);
        xAxis.VisibleRange = new DoubleRange(0, duration);
        yAxis.VisibleRange = new DoubleRange(0, Math.Max(1, names.Length));
        emptyMessage.Text = viewModel?.StatusText ?? "已加载";
        emptyMessage.Visibility = frame.Segments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        using (surface.SuspendUpdates())
        {
            for (var traceIndex = 0; traceIndex < traceSeries.Count; traceIndex++)
            {
                UpdateSeries(traceSeries[traceIndex], frame, traceIndex, names.Length);
            }
        }
    }

    private void EnsureTraceSeries(IReadOnlyList<string> names)
    {
        if (activeLabels.SequenceEqual(names))
        {
            return;
        }

        activeLabels = names.ToArray();
        surface.RenderableSeries.Clear();
        traceSeries.Clear();
        labels.Children.Clear();
        labels.RowDefinitions.Clear();
        for (var index = 0; index < names.Count; index++)
        {
            var data = new XyDataSeries<double, double>();
            surface.RenderableSeries.Add(new FastLineRenderableSeries
            {
                DataSeries = data,
                Stroke = TraceColors[index % TraceColors.Length],
                StrokeThickness = 1,
            });
            traceSeries.Add(new TraceSeries(data));
            labels.RowDefinitions.Add(new RowDefinition());
            var label = new TextBlock
            {
                Text = names[index],
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0),
            };
            Grid.SetRow(label, index);
            labels.Children.Add(label);
        }
    }

    private void UpdateSeries(TraceSeries series, RecordingReviewFrame frame, int traceIndex, int traceCount)
    {
        var xValues = new List<double>();
        var yValues = new List<double>();
        var baseline = traceCount - traceIndex - 0.5;
        var hasSegment = false;
        foreach (var segment in frame.Segments)
        {
            if (traceIndex >= segment.Channels.Count)
            {
                continue;
            }

            if (hasSegment)
            {
                xValues.Add(double.NaN);
                yValues.Add(double.NaN);
            }

            AppendDecimated(
                xValues,
                yValues,
                segment,
                segment.Channels[traceIndex].Values,
                frame,
                baseline);
            hasSegment = true;
        }

        using (series.Data.SuspendUpdates())
        {
            series.Data.Clear();
            if (xValues.Count > 0)
            {
                series.Data.Append(xValues, yValues);
            }
        }
    }

    private static void AppendDecimated(
        List<double> xValues,
        List<double> yValues,
        ProjectedMontageSegment segment,
        IReadOnlyList<double> values,
        RecordingReviewFrame frame,
        double baseline)
    {
        var bucketSize = Math.Max(1, (int)Math.Ceiling(values.Count / (double)MaximumPointsPerTrace));
        for (var start = 0; start < values.Count; start += bucketSize)
        {
            var end = Math.Min(values.Count, start + bucketSize);
            var first = -1;
            var last = -1;
            var min = -1;
            var max = -1;
            for (var index = start; index < end; index++)
            {
                if (!double.IsFinite(values[index]))
                {
                    continue;
                }

                first = first < 0 ? index : first;
                last = index;
                if (min < 0 || values[index] < values[min]) min = index;
                if (max < 0 || values[index] > values[max]) max = index;
            }

            foreach (var index in new[] { first, min, max, last }.Distinct().Where(index => index >= 0).OrderBy(index => index))
            {
                var seconds = (segment.FirstSampleCounter + index - frame.WindowStartSampleCounter) / (double)frame.SamplingRateHz;
                xValues.Add(seconds);
                yValues.Add(baseline + values[index] * 1_000_000d / 100d);
            }
        }
    }

    private void ClearSeries()
    {
        foreach (var series in traceSeries)
        {
            series.Data.Clear();
        }
    }

    private sealed record TraceSeries(XyDataSeries<double, double> Data);
}
