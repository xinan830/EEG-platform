using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SciChart.Charting;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

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
    private readonly RecordingTimeLabelProvider recordingTimeLabels = new();
    private readonly WallClockSecondTickProvider wallClockTicks = new();
    private readonly NumericAxis yAxis = new();
    private readonly Grid labels = new();
    private readonly Grid chartHost = new();
    private readonly Canvas eventMarkers = new();
    private readonly Border playbackCursor = new()
    {
        Width = 1,
        Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
        HorizontalAlignment = HorizontalAlignment.Left,
        IsHitTestVisible = false,
    };
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
    private Thickness? lastLabelPlotMargin;
    private Window? owningWindow;
    private ScreenScaleContext screenScale = ScreenScaleContext.Nominal;

    public RecordingReviewSciChartCanvas()
    {
        ConfigureSurface();
        var layout = new Grid();
        // Match the live canvas: channel names occupy only the width they need,
        // rather than leaving a wider review-only gutter before the waveform.
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(labels, 0);
        chartHost.Children.Add(surface);
        chartHost.Children.Add(emptyMessage);
        chartHost.Children.Add(eventMarkers);
        chartHost.Children.Add(playbackCursor);
        chartHost.SizeChanged += (_, _) =>
        {
            var viewModel = DataContext as RecordingReviewViewModel;
            viewModel?.UpdateViewportWidth(chartHost.ActualWidth);
            ApplyFrame(viewModel?.CurrentFrame, viewModel);
        };
        Grid.SetColumn(chartHost, 1);
        layout.Children.Add(labels);
        layout.Children.Add(chartHost);
        Content = layout;
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
        surface.LayoutUpdated += (_, _) => SyncLabelPlotArea();
    }

    private void ConfigureSurface()
    {
        surface.Background = Brushes.White;
        VisualXcceleratorEngine.SetIsEnabled(surface, true);
        WaveformAxisPolicy.ConfigureHorizontalAxis(xAxis, recordingTimeLabels);
        xAxis.TickProvider = wallClockTicks;
        xAxis.MajorGridLineStyle = new Style(typeof(Line))
        {
            Setters = { new Setter(Shape.StrokeProperty, new SolidColorBrush(Color.FromRgb(148, 163, 184))) },
        };
        // Keep the invisible Y-axis layout area away from the channel-label
        // column so review uses the same compact waveform start as live view.
        WaveformAxisPolicy.ConfigureStackedTraceAxis(yAxis);
        surface.XAxes.Add(xAxis);
        surface.YAxes.Add(yAxis);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Unsubscribe(e.OldValue as RecordingReviewViewModel);
        Subscribe(e.NewValue as RecordingReviewViewModel);
        (e.NewValue as RecordingReviewViewModel)?.UpdateScreenScale(screenScale);
        (e.NewValue as RecordingReviewViewModel)?.UpdateViewportWidth(chartHost.ActualWidth);
        ApplyFrame((e.NewValue as RecordingReviewViewModel)?.CurrentFrame, e.NewValue as RecordingReviewViewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        owningWindow = Window.GetWindow(this);
        if (owningWindow is not null)
        {
            owningWindow.LocationChanged += OnWindowDisplayContextChanged;
            UpdateScreenScale();
        }

        ScreenCalibrationMetrics.CalibrationChanged += OnCalibrationChanged;
        (DataContext as RecordingReviewViewModel)?.UpdateViewportWidth(chartHost.ActualWidth);
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        ScreenCalibrationMetrics.CalibrationChanged -= OnCalibrationChanged;
        if (owningWindow is not null)
        {
            owningWindow.LocationChanged -= OnWindowDisplayContextChanged;
            owningWindow = null;
        }

        Unsubscribe();
    }

    private void OnWindowDisplayContextChanged(object? sender, EventArgs eventArgs) => UpdateScreenScale();

    private void OnCalibrationChanged(object? sender, EventArgs eventArgs) => UpdateScreenScale();

    private void UpdateScreenScale()
    {
        if (owningWindow is null)
        {
            return;
        }

        screenScale = ScreenCalibrationMetrics.ResolveScale(owningWindow);
        (DataContext as RecordingReviewViewModel)?.UpdateScreenScale(screenScale);
        ApplyFrame((DataContext as RecordingReviewViewModel)?.CurrentFrame, DataContext as RecordingReviewViewModel);
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
        if (e.PropertyName is nameof(RecordingReviewViewModel.CurrentFrame)
            or nameof(RecordingReviewViewModel.VisibleDurationSeconds)
            or nameof(RecordingReviewViewModel.SensitivityMicrovoltsPerMillimeter)
            or nameof(RecordingReviewViewModel.RecordingEvents))
        {
            ApplyFrame((sender as RecordingReviewViewModel)?.CurrentFrame, sender as RecordingReviewViewModel);
        }
        else if (e.PropertyName is nameof(RecordingReviewViewModel.PositionSeconds)
                 or nameof(RecordingReviewViewModel.ViewportStartSeconds))
        {
            UpdateVisibleRange(sender as RecordingReviewViewModel);
            UpdatePlaybackCursor(sender as RecordingReviewViewModel);
            UpdateEventMarkers(sender as RecordingReviewViewModel);
            UpdateLifecycleBoundaries(sender as RecordingReviewViewModel);
        }
    }

    private void ApplyFrame(RecordingReviewFrame? frame, RecordingReviewViewModel? viewModel)
    {
        recordingTimeLabels.RecordingStartUtc = viewModel?.RecordingStartUtc;
        wallClockTicks.OriginUtc = viewModel?.RecordingStartUtc;
        if (frame is null)
        {
            emptyMessage.Text = viewModel?.StatusText ?? "等待加载回溯数据";
            emptyMessage.Visibility = Visibility.Visible;
            ClearSeries();
            eventMarkers.Children.Clear();
            playbackCursor.Visibility = Visibility.Collapsed;
            return;
        }

        var names = frame.OutputChannelNames.ToArray();
        EnsureTraceSeries(names);
        UpdateVisibleRange(viewModel);
        xAxis.InvalidateElement();
        yAxis.VisibleRange = new DoubleRange(0, Math.Max(1, names.Length));
        SyncLabelPlotArea();
        emptyMessage.Text = viewModel?.StatusText ?? "已加载";
        emptyMessage.Visibility = frame.Segments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        using (surface.SuspendUpdates())
        {
            for (var traceIndex = 0; traceIndex < traceSeries.Count; traceIndex++)
            {
                UpdateSeries(traceSeries[traceIndex], frame, traceIndex, names.Length);
            }
        }

        UpdatePlaybackCursor(viewModel);
        UpdateEventMarkers(viewModel);
        UpdateLifecycleBoundaries(viewModel);
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
                Margin = new Thickness(2, 0, 0, 0),
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
        // The plot is laid out in DIP, so sensitivity must use the calibrated
        // physical DIP/mm conversion, not the monitor's nominal raster DPI.
        var plotHeight = WaveformPlotLayout.GetPlotArea(surface).Height;
        var sensitivity = (DataContext as RecordingReviewViewModel)?.SensitivityMicrovoltsPerMillimeter ?? 10;
        var displayScale = ScreenScaleCalculator.VerticalDisplayScale(
            traceCount,
            plotHeight,
            sensitivity,
            screenScale.MillimetersPerDipY);
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
                baseline,
                displayScale);
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
        double baseline,
        double displayScale)
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
                var seconds = frame.WindowStartSeconds +
                    (segment.FirstSampleCounter + index - frame.WindowStartSampleCounter) / (double)frame.SamplingRateHz;
                xValues.Add(seconds);
                yValues.Add(baseline + values[index] * 1_000_000d * displayScale);
            }
        }
    }

    private void UpdatePlaybackCursor(RecordingReviewViewModel? viewModel)
    {
        if (viewModel?.CurrentFrame is not { } frame || chartHost.ActualWidth <= 0)
        {
            playbackCursor.Visibility = Visibility.Collapsed;
            return;
        }

        var visibleStart = viewModel.ViewportStartSeconds;
        var visibleEnd = Math.Min(viewModel.DurationSeconds, visibleStart + viewModel.VisibleDurationSeconds);
        var duration = Math.Max(0.001, visibleEnd - visibleStart);
        var fraction = Math.Clamp((viewModel.PositionSeconds - visibleStart) / duration, 0, 1);
        playbackCursor.Margin = new Thickness(fraction * Math.Max(0, chartHost.ActualWidth - 1), 0, 0, 0);
        playbackCursor.Visibility = viewModel.PositionSeconds >= visibleStart &&
                                    viewModel.PositionSeconds <= visibleEnd
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateEventMarkers(RecordingReviewViewModel? viewModel)
    {
        eventMarkers.Children.Clear();
        if (viewModel is null || chartHost.ActualWidth <= 0 || chartHost.ActualHeight <= 0)
            return;

        var start = viewModel.ViewportStartSeconds;
        var end = Math.Min(viewModel.DurationSeconds, start + viewModel.VisibleDurationSeconds);
        var duration = Math.Max(0.001, end - start);
        foreach (var item in viewModel.RecordingEvents)
        {
            if (!item.IsDisplayable)
            {
                continue;
            }

            var itemStart = RecordingTimeMapper.ToElapsedSeconds(item.StartSample, viewModel.SamplingRateHz);
            var itemEnd = item.IsInterval
                ? RecordingTimeMapper.ToElapsedSeconds(item.EndSampleExclusive, viewModel.SamplingRateHz)
                : itemStart;
            if (itemEnd < start || itemStart > end) continue;
            var left = Math.Clamp((itemStart - start) / duration, 0, 1) * chartHost.ActualWidth;
            var right = Math.Clamp((itemEnd - start) / duration, 0, 1) * chartHost.ActualWidth;
            var color = ParseColor(item.DefinitionSnapshot.Color);
            eventMarkers.Children.Add(new Border
            {
                ToolTip = RecordingEventDisplayTime.FormatMarker(item, viewModel.RecordingStartUtc, viewModel.SamplingRateHz),
                IsHitTestVisible = !item.IsInterval,
                Width = item.IsInterval ? Math.Max(2, right - left) : 2,
                Height = chartHost.ActualHeight,
                Background = item.IsInterval
                    ? new SolidColorBrush(Color.FromArgb(32, color.R, color.G, color.B))
                    : new SolidColorBrush(color),
                BorderBrush = item.IsInterval ? new SolidColorBrush(Color.FromArgb(150, color.R, color.G, color.B)) : null,
                BorderThickness = item.IsInterval ? new Thickness(1, 0, 1, 0) : new Thickness(0),
                Margin = new Thickness(left, 0, 0, 0),
            });
            WaveformEventTimeLabel.Add(
                eventMarkers,
                left,
                3,
                RecordingClockLabelFormatter.FormatSampleMilliseconds(
                    viewModel.RecordingStartUtc, item.StartSample, viewModel.SamplingRateHz),
                color,
                RecordingEventDisplayTime.FormatMarker(item, viewModel.RecordingStartUtc, viewModel.SamplingRateHz));
        }
    }

    private static Color ParseColor(string value)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch { return Color.FromRgb(37, 99, 235); }
    }

    private void UpdateLifecycleBoundaries(RecordingReviewViewModel? viewModel)
    {
        foreach (var line in eventMarkers.Children.OfType<Line>().ToArray())
        {
            eventMarkers.Children.Remove(line);
        }

        if (viewModel is null || chartHost.ActualWidth <= 0 || chartHost.ActualHeight <= 0)
            return;

        var start = viewModel.ViewportStartSeconds;
        var end = Math.Min(viewModel.DurationSeconds, start + viewModel.VisibleDurationSeconds);
        var duration = Math.Max(0.001, end - start);
        var plotArea = WaveformPlotLayout.GetPlotArea(surface);
        foreach (var boundary in viewModel.LifecycleBoundaries)
        {
            var relativeSample = boundary.SampleCounter - viewModel.RecordingFirstSampleCounter;
            var seconds = relativeSample / (double)viewModel.SamplingRateHz;
            if (seconds < start || seconds > end) continue;
            var x = plotArea.Left + Math.Clamp((seconds - start) / duration, 0, 1) * plotArea.Width;
            eventMarkers.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = plotArea.Top, Y2 = plotArea.Bottom,
                Stroke = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                IsHitTestVisible = false,
            });
            WaveformEventTimeLabel.Add(
                eventMarkers,
                x,
                plotArea.Top + 3,
                RecordingClockLabelFormatter.FormatSampleMilliseconds(
                    viewModel.RecordingStartUtc, relativeSample, viewModel.SamplingRateHz),
                Color.FromRgb(51, 65, 85),
                BoundaryText(boundary.Kind));
        }
    }

    private static string BoundaryText(RecordingLifecycleBoundaryKind kind) => kind switch
    {
        RecordingLifecycleBoundaryKind.Started => "开始记录",
        RecordingLifecycleBoundaryKind.Paused => "暂停记录",
        RecordingLifecycleBoundaryKind.Resumed => "恢复记录",
        RecordingLifecycleBoundaryKind.Stopped => "结束记录",
        _ => kind.ToString(),
    };

    private void UpdateVisibleRange(RecordingReviewViewModel? viewModel)
    {
        if (viewModel is null)
        {
            return;
        }

        var start = viewModel.ViewportStartSeconds;
        var end = Math.Max(start + 0.001, Math.Min(viewModel.DurationSeconds, start + viewModel.VisibleDurationSeconds));
        // Keep the last complete frame on screen while a new window is being
        // read. Moving the axis ahead of the frame creates a false white gap.
        if (viewModel.CurrentFrame is not { } frame)
        {
            return;
        }

        if (frame.WindowStartSeconds <= start + 0.000_001 &&
            frame.WindowEndSeconds + 0.000_001 >= end)
        {
            xAxis.VisibleRange = new DoubleRange(start, end);
            return;
        }

        // A frame is a cache block and can be wider than the requested screen
        // window. Never use its range as the display scale: paper speed and
        // timebase must control the axis even while the next block is loading.
        xAxis.VisibleRange = new DoubleRange(start, end);
    }

    private void ClearSeries()
    {
        foreach (var series in traceSeries)
        {
            series.Data.Clear();
        }
    }

    private void SyncLabelPlotArea()
    {
        WaveformPlotLayout.SyncLabelPlotArea(surface, labels, ref lastLabelPlotMargin);
    }

    private sealed record TraceSeries(XyDataSeries<double, double> Data);
}
