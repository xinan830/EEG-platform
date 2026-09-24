using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SciChart.Charting;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Annotations;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Acquisition.Waveform;

/// <summary>
/// SciChart host for raw acquisition display. It does not own the device,
/// mutate batches, or apply scientific processing.
/// </summary>
public sealed class LiveSciChartCanvas : UserControl
{
    private const int MaximumRenderBuckets = 1_000;
    private const double EraseBandDurationSeconds = 0.3d;
    private static readonly Color[] TraceColors =
    [
        Color.FromRgb(37, 99, 235), Color.FromRgb(2, 132, 199), Color.FromRgb(22, 163, 74), Color.FromRgb(245, 158, 11),
        Color.FromRgb(239, 68, 68), Color.FromRgb(147, 51, 234), Color.FromRgb(71, 85, 105), Color.FromRgb(8, 145, 178),
    ];

    private readonly SciChartSurface surface = new();
    private readonly NumericAxis xAxis = new();
    private readonly NumericAxis yAxis = new();
    private readonly SweepTimeLabelProvider sweepTimeLabels = new();
    private readonly WallClockSecondTickProvider wallClockTicks = new();
    private readonly SweepEraseBandAnimator eraseBandAnimator = new();
    private readonly BoxAnnotation eraseBand = new()
    {
        Background = Brushes.White,
        BorderBrush = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        IsEditable = false,
        IsHidden = true,
    };
    private readonly BoxAnnotation eraseWrapBand = new()
    {
        Background = Brushes.White,
        BorderBrush = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        IsEditable = false,
        IsHidden = true,
    };
    private readonly Grid channelLabels = new();
    private readonly Grid chartHost = new();
    private readonly Canvas eventMarkers = new();
    private readonly TextBlock emptyMessage = new()
    {
        Text = "等待设备连接并开始记录",
        FontSize = 14,
        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly List<TraceSeries> traceSeries = [];
    private DispatcherTimer? refreshTimer;
    private DispatcherTimer? eraseAnimationTimer;
    private LatestWaveformFrameWorker? frameWorker;
    private string[] activeLabels = [];
    private WaveformRenderRevision? lastRequestedRevision;
    private long renderSequence;
    private WaveformDisplayFrame? currentFrame;
    private double activeDisplayWindowSeconds;
    private int activeTraceCount;
    private Thickness? lastLabelPlotMargin;
    private Window? owningWindow;
    private ScreenScaleContext screenScale = ScreenScaleContext.Nominal;

    public LiveSciChartCanvas()
    {
        ConfigureSurface();
        Content = CreateLayout();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += (_, _) =>
        {
            UpdateScreenScale();
            RequestRefresh();
        };
        SizeChanged += (_, _) => RequestRefresh();
        surface.LayoutUpdated += (_, _) => SyncLabelPlotArea();
    }

    private void ConfigureSurface()
    {
        surface.Background = Brushes.White;
        VisualXcceleratorEngine.SetIsEnabled(surface, true);

        WaveformAxisPolicy.ConfigureHorizontalAxis(xAxis, sweepTimeLabels);
        xAxis.TickProvider = wallClockTicks;
        xAxis.TickTextBrush = new SolidColorBrush(Color.FromRgb(49, 77, 126));
        xAxis.MajorGridLineStyle = new Style(typeof(Line))
        {
            Setters = { new Setter(Shape.StrokeProperty, new SolidColorBrush(Color.FromRgb(226, 232, 240))) },
        };

        // The Y axis has no labels or ticks in the EEG stacked-trace view.
        // Keeping its layout slot at the left creates a visible blank gutter
        // between montage labels and the first waveform sample. Put that
        // invisible slot at the far edge instead.
        WaveformAxisPolicy.ConfigureStackedTraceAxis(yAxis);

        surface.XAxes.Add(xAxis);
        surface.YAxes.Add(yAxis);
        surface.Annotations.Add(eraseBand);
        surface.Annotations.Add(eraseWrapBand);
    }

    private Grid CreateLayout()
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(channelLabels, 0);
        chartHost.Children.Add(surface);
        chartHost.Children.Add(emptyMessage);
        chartHost.Children.Add(eventMarkers);
        Grid.SetColumn(chartHost, 1);
        layout.Children.Add(channelLabels);
        layout.Children.Add(chartHost);
        return layout;
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        AttachWindowContext();
        ScreenCalibrationMetrics.CalibrationChanged += OnCalibrationChanged;
        frameWorker = new LatestWaveformFrameWorker(Dispatcher, ApplyFrameResult);
        refreshTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        refreshTimer.Tick += (_, _) => RequestRefresh();
        refreshTimer.Start();
        eraseAnimationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        eraseAnimationTimer.Tick += (_, _) => AdvanceEraseBand();
        eraseAnimationTimer.Start();
        RequestRefresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        ScreenCalibrationMetrics.CalibrationChanged -= OnCalibrationChanged;
        DetachWindowContext();
        refreshTimer?.Stop();
        refreshTimer = null;
        eraseAnimationTimer?.Stop();
        eraseAnimationTimer = null;
        eraseBandAnimator.Reset();
        frameWorker?.Dispose();
        frameWorker = null;
        lastRequestedRevision = null;
    }

    private void AttachWindowContext()
    {
        owningWindow = Window.GetWindow(this);
        if (owningWindow is not null)
        {
            owningWindow.LocationChanged += OnWindowDisplayContextChanged;
            UpdateScreenScale();
        }
    }

    private void DetachWindowContext()
    {
        if (owningWindow is not null)
        {
            owningWindow.LocationChanged -= OnWindowDisplayContextChanged;
            owningWindow = null;
        }
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
        (DataContext as LiveMonitoringViewModel)?.UpdateScreenScale(screenScale);
        RequestRefresh();
    }

    private void RequestRefresh()
    {
        var monitor = DataContext as LiveMonitoringViewModel;
        var source = monitor?.GetWaveformSource();
        if (monitor is null || source is null)
        {
            frameWorker?.CancelPending();
            lastRequestedRevision = null;
            ShowEmptyState(monitor);
            return;
        }

        if (frameWorker is null)
        {
            return;
        }

        var viewportWidthDips = Math.Max(1d, surface.ActualWidth);
        monitor.UpdateViewportWidth(viewportWidthDips);
        var horizontalPixels = Math.Clamp((int)Math.Round(viewportWidthDips), 1, MaximumRenderBuckets);
        var plotHeight = WaveformPlotLayout.GetPlotArea(surface).Height;
        var displayWindowSeconds = monitor.GetDisplayWindowSeconds(viewportWidthDips);
        var revision = WaveformRenderRevision.Create(
            source,
            displayWindowSeconds,
            horizontalPixels,
            monitor.SensitivityMicrovoltsPerMillimeter,
            plotHeight);
        if (revision == lastRequestedRevision)
        {
            UpdateEventMarkers(currentFrame, monitor);
            return;
        }

        lastRequestedRevision = revision;
        frameWorker.Request(new WaveformFrameBuildRequest(
            ++renderSequence,
            source,
            displayWindowSeconds,
            horizontalPixels,
            monitor.SensitivityMicrovoltsPerMillimeter,
            plotHeight));
    }

    private void ApplyFrameResult(WaveformFrameBuildResult result)
    {
        if (result.Failure is not null)
        {
            emptyMessage.Text = $"波形显示失败：{result.Failure.Message}";
            emptyMessage.Visibility = Visibility.Visible;
            return;
        }

        if (result.Frame is not { } frame)
        {
            currentFrame = null;
            ShowEmptyState(DataContext as LiveMonitoringViewModel);
            return;
        }

        currentFrame = frame;
        emptyMessage.Text = "等待设备连接并开始采集";
        emptyMessage.Visibility = frame.Traces.All(trace => trace.Points.Count == 0)
            ? Visibility.Visible
            : Visibility.Collapsed;
        EnsureTraceSeries(frame.Traces);
        var windowSeconds = frame.DisplayWindowSeconds;
        xAxis.VisibleRange = new DoubleRange(0, windowSeconds);
        xAxis.MajorDelta = 1d;
        xAxis.MinorDelta = 0.5d;
        var cursorPosition = Math.Min(frame.CursorSeconds, Math.Max(0, windowSeconds - 0.001d));
        var clockAnchor = (DataContext as LiveMonitoringViewModel)?.GetDisplayClockAnchorUtc();
        sweepTimeLabels.Update(frame.PageStartElapsedSeconds, frame.CursorSeconds, clockAnchor);
        wallClockTicks.OriginUtc = clockAnchor?.AddSeconds(frame.PageStartElapsedSeconds);
        xAxis.InvalidateElement();
        activeDisplayWindowSeconds = windowSeconds;
        activeTraceCount = frame.Traces.Count;
        SyncLabelPlotArea();
        var displayedCursorPosition = eraseBandAnimator.SetTarget(
            frame.PageStartElapsedSeconds,
            cursorPosition,
            windowSeconds,
            Environment.TickCount64);
        UpdateEraseBand(displayedCursorPosition, activeDisplayWindowSeconds, activeTraceCount);
        yAxis.VisibleRange = new DoubleRange(0, frame.Traces.Count);
        // SciChart/WPF layout sizes are DIP. Use the calibrated physical
        // conversion instead of raster DPI, which is only a nominal OS scale
        // and cannot represent the user's measured screen dimensions.
        var displayScale = ScreenScaleCalculator.VerticalDisplayScale(
            frame.Traces.Count,
            result.Request.PlotHeight,
            result.Request.SensitivityMicrovoltsPerMillimeter,
            screenScale.MillimetersPerDipY);

        using (surface.SuspendUpdates())
        {
            for (var index = 0; index < frame.Traces.Count; index++)
            {
                UpdateSeries(traceSeries[index], frame, frame.Traces[index], index, displayScale);
            }
        }
        UpdateEventMarkers(frame, DataContext as LiveMonitoringViewModel);
    }

    private void ShowEmptyState(LiveMonitoringViewModel? monitor)
    {
        emptyMessage.Text = monitor?.WaveformStatusText ?? "等待设备连接并开始采集";
        emptyMessage.Visibility = Visibility.Visible;
        eraseBand.IsHidden = true;
        eraseWrapBand.IsHidden = true;
        eventMarkers.Children.Clear();
        currentFrame = null;
        activeDisplayWindowSeconds = 0;
        activeTraceCount = 0;
        eraseBandAnimator.Reset();
        sweepTimeLabels.Update(0, 0, null);
        wallClockTicks.OriginUtc = null;
        xAxis.InvalidateElement();
        var labels = monitor?.VisibleChannelLabels ?? [];
        if (labels.Count == 0)
        {
            activeLabels = [];
            traceSeries.Clear();
            surface.RenderableSeries.Clear();
            return;
        }

        EnsureTraceSeries(labels.Select(label => new WaveformDisplayTrace(label, [])).ToArray());
        var viewportWidthDips = Math.Max(1d, surface.ActualWidth);
        monitor!.UpdateViewportWidth(viewportWidthDips);
        var windowSeconds = monitor.GetDisplayWindowSeconds(viewportWidthDips);
        xAxis.VisibleRange = new DoubleRange(0, windowSeconds);
        xAxis.MajorDelta = 1d;
        xAxis.MinorDelta = 0.5d;
        yAxis.VisibleRange = new DoubleRange(0, labels.Count);
        using (surface.SuspendUpdates())
        {
            foreach (var trace in traceSeries)
            {
                trace.Data.Clear();
            }
        }
    }

    private void UpdateEraseBand(
        double cursorPosition,
        double windowSeconds,
        int traceCount)
    {
        if (windowSeconds <= 0 || traceCount <= 0)
        {
            eraseBand.IsHidden = true;
            eraseWrapBand.IsHidden = true;
            return;
        }

        // The eraser is a time range, not a fixed-pixel cursor. It covers the
        // next 0.3 seconds of the cyclic page and wraps at the right edge.
        var duration = Math.Min(EraseBandDurationSeconds, windowSeconds);
        var firstDuration = Math.Min(duration, windowSeconds - cursorPosition);
        SetEraseSegment(eraseBand, cursorPosition, firstDuration, traceCount);
        var wrappedDuration = duration - firstDuration;
        SetEraseSegment(eraseWrapBand, 0, wrappedDuration, traceCount);
    }

    private void AdvanceEraseBand()
    {
        if (eraseBandAnimator.TryAdvance(Environment.TickCount64, out var cursorPosition))
        {
            UpdateEraseBand(cursorPosition, activeDisplayWindowSeconds, activeTraceCount);
        }
    }

    private void SetEraseSegment(
        BoxAnnotation annotation,
        double startSeconds,
        double durationSeconds,
        int traceCount)
    {
        if (durationSeconds <= 0)
        {
            annotation.IsHidden = true;
            return;
        }

        annotation.X1 = startSeconds;
        annotation.X2 = startSeconds + durationSeconds;
        annotation.Y1 = 0d;
        annotation.Y2 = (double)traceCount;
        annotation.IsHidden = false;
    }

    private void UpdateEventMarkers(WaveformDisplayFrame? frame, LiveMonitoringViewModel? monitor)
    {
        eventMarkers.Children.Clear();
        if (frame is null || monitor is null || eventMarkers.ActualWidth <= 0 || eventMarkers.ActualHeight <= 0)
        {
            return;
        }

        var pageStart = frame.WindowStartSampleCounter;
        var pageEndExclusive = checked(pageStart + frame.WindowSampleCount);
        var plotArea = WaveformPlotLayout.GetPlotArea(surface);
        if (plotArea.Width <= 0 || plotArea.Height <= 0)
        {
            return;
        }

        foreach (var item in monitor.LiveRecordingEvents)
        {
            if (!item.IsDisplayable)
            {
                continue;
            }

            var start = monitor.ToDisplayCounterFromRecordingSample(item.StartSample);
            if (start is null)
            {
                continue;
            }

            var end = item.IsInterval
                ? monitor.ToDisplayCounterFromRecordingSample(item.EndSampleExclusive)
                : start;
            if (start.Value < pageStart || start.Value >= pageEndExclusive || end is null)
            {
                continue;
            }

            var leftFraction = (start.Value - pageStart) / (double)frame.WindowSampleCount;
            var rightFraction = item.IsInterval
                ? Math.Clamp((end.Value - pageStart) / (double)frame.WindowSampleCount, leftFraction, 1d)
                : leftFraction;
            var color = ParseColor(item.DefinitionSnapshot.Color);
            var marker = new Border
            {
                ToolTip = monitor.FormatEventMarker(item),
                IsHitTestVisible = !item.IsInterval,
                Width = item.IsInterval
                    ? Math.Max(2d, (rightFraction - leftFraction) * plotArea.Width)
                    : 2d,
                Height = plotArea.Height,
                Background = item.IsInterval
                    ? new SolidColorBrush(Color.FromArgb(32, color.R, color.G, color.B))
                    : new SolidColorBrush(color),
                BorderBrush = item.IsInterval ? new SolidColorBrush(Color.FromArgb(150, color.R, color.G, color.B)) : null,
                BorderThickness = item.IsInterval ? new Thickness(1, 0, 1, 0) : new Thickness(0),
            };
            Canvas.SetLeft(marker, plotArea.Left + leftFraction * plotArea.Width);
            Canvas.SetTop(marker, plotArea.Top);
            eventMarkers.Children.Add(marker);
            if (monitor.FormatEventClockTime(item) is { } clockTime)
            {
                WaveformEventTimeLabel.Add(
                    eventMarkers,
                    plotArea.Left + leftFraction * plotArea.Width,
                    plotArea.Top + 3,
                    clockTime,
                    color,
                    monitor.FormatEventMarker(item));
            }
        }
    }

    private static Color ParseColor(string value)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch { return Color.FromRgb(37, 99, 235); }
    }

    private void EnsureTraceSeries(IReadOnlyList<WaveformDisplayTrace> traces)
    {
        var labels = traces.Select(trace => trace.Label).ToArray();
        if (activeLabels.SequenceEqual(labels))
        {
            return;
        }

        activeLabels = labels;
        surface.RenderableSeries.Clear();
        traceSeries.Clear();
        for (var index = 0; index < traces.Count; index++)
        {
            var data = new XyDataSeries<double, double>();
            surface.RenderableSeries.Add(new FastLineRenderableSeries
            {
                DataSeries = data,
                Stroke = TraceColors[index % TraceColors.Length],
                StrokeThickness = 1,
            });
            traceSeries.Add(new TraceSeries(data));
        }

        RebuildLabels(labels);
    }

    private static void UpdateSeries(
        TraceSeries series,
        WaveformDisplayFrame frame,
        WaveformDisplayTrace trace,
        int traceIndex,
        double displayScale)
    {
        using (series.Data.SuspendUpdates())
        {
            series.Data.Clear();
            var baseline = frame.Traces.Count - traceIndex - .5d;
            var hasPreviousPoint = false;
            var xValues = series.XValues;
            var yValues = series.YValues;
            xValues.Clear();
            yValues.Clear();
            xValues.EnsureCapacity(trace.Points.Count * 2);
            yValues.EnsureCapacity(trace.Points.Count * 2);
            foreach (var point in trace.Points)
            {
                var seconds = point.DisplaySampleOffset / (double)frame.SamplingRateHz;
                if (point.StartsSegment && hasPreviousPoint)
                {
                    xValues.Add(Math.Max(0, seconds - .000001d));
                    yValues.Add(double.NaN);
                }

                xValues.Add(seconds);
                yValues.Add(baseline + point.MinVolts * 1_000_000d * displayScale);
                hasPreviousPoint = true;
            }

            if (xValues.Count > 0)
            {
                series.Data.Append(xValues, yValues);
            }
        }
    }

    private void RebuildLabels(IReadOnlyList<string> labels)
    {
        channelLabels.Children.Clear();
        channelLabels.RowDefinitions.Clear();
        for (var index = 0; index < labels.Count; index++)
        {
            channelLabels.RowDefinitions.Add(new RowDefinition());
            var channelLabel = new TextBlock
            {
                Text = labels[index],
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 4, 0),
            };
            Grid.SetRow(channelLabel, index);
            channelLabels.Children.Add(channelLabel);
        }
    }

    private void SyncLabelPlotArea()
    {
        WaveformPlotLayout.SyncLabelPlotArea(surface, channelLabels, ref lastLabelPlotMargin);
    }

    private sealed class TraceSeries(XyDataSeries<double, double> data)
    {
        public XyDataSeries<double, double> Data { get; } = data;

        public List<double> XValues { get; } = [];

        public List<double> YValues { get; } = [];
    }

}
