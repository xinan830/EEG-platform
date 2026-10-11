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
    private readonly Grid rootLayout = WaveformPlotLayout.CreateRootGrid();
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
    private string? lastMarkerLayoutSignature;
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
        xAxis.TickTextBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105));
        xAxis.FontSize = 11;
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
        var layout = rootLayout;

        channelLabels.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(channelLabels, 0);
        chartHost.Children.Add(surface);
        chartHost.Children.Add(emptyMessage);
        chartHost.Children.Add(eventMarkers);
        Grid.SetColumn(chartHost, 2);
        chartHost.Margin = new Thickness(0);
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
            // 30 fps keeps the sweep visually continuous while leaving a
            // larger scheduling budget than a 60 fps display refresh.
            Interval = TimeSpan.FromMilliseconds(33),
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

        var pageStart = WaveformDisplayFrameBuilder.GetPageStartSampleCounter(source, displayWindowSeconds);
        var prepareSeriesForPageSwap = pageStart is not null &&
            (currentFrame is null || pageStart.Value != currentFrame.WindowStartSampleCounter);
        lastRequestedRevision = revision;
        frameWorker.Request(new WaveformFrameBuildRequest(
            ++renderSequence,
            source,
            displayWindowSeconds,
            horizontalPixels,
            monitor.SensitivityMicrovoltsPerMillimeter,
            plotHeight,
            screenScale.MillimetersPerDipY,
            prepareSeriesForPageSwap));
    }

    private void ApplyFrameResult(WaveformFrameBuildResult result)
    {
        if (result.Failure is not null)
        {
            if (!result.IsLatest && currentFrame is not null)
            {
                return;
            }

            emptyMessage.Text = $"波形显示失败：{result.Failure.Message}";
            emptyMessage.Visibility = Visibility.Visible;
            return;
        }

        if (result.Frame is not { } frame)
        {
            // A source snapshot can be empty while the device/filter pipeline
            // is transitioning. Keep the last complete frame visible; the raw
            // recording remains the source of truth and real gaps are still
            // represented by NaN segment breaks in a committed frame.
            if (currentFrame is null)
            {
                ShowEmptyState(DataContext as LiveMonitoringViewModel);
            }
            return;
        }

        if (!result.IsLatest && currentFrame is not null &&
            ShouldRejectOlderFrame(currentFrame, frame))
        {
            return;
        }

        var previousFrame = currentFrame;
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
                if (result.PreparedSeries is { Count: > 0 } prepared &&
                    frame.WindowStartSampleCounter != previousFrame?.WindowStartSampleCounter &&
                    index < prepared.Count)
                {
                    traceSeries[index].ReplaceData(prepared[index]);
                }
                else
                {
                    UpdateSeries(traceSeries[index], frame, frame.Traces[index], index, displayScale);
                }
            }
        }
        if (UpdateEventMarkers(frame, DataContext as LiveMonitoringViewModel))
        {
            UpdateLifecycleBoundaries(frame, DataContext as LiveMonitoringViewModel);
        }
    }

    private static bool ShouldRejectOlderFrame(
        WaveformDisplayFrame current,
        WaveformDisplayFrame candidate)
    {
        var currentLabels = current.Traces.Select(trace => trace.Label);
        var candidateLabels = candidate.Traces.Select(trace => trace.Label);
        if (!currentLabels.SequenceEqual(candidateLabels, StringComparer.Ordinal))
        {
            return false;
        }

        if (candidate.WindowStartSampleCounter < current.WindowStartSampleCounter)
        {
            return true;
        }

        return candidate.WindowStartSampleCounter == current.WindowStartSampleCounter &&
            candidate.CursorSeconds + 0.000001d < current.CursorSeconds;
    }

    private void ShowEmptyState(LiveMonitoringViewModel? monitor)
    {
        emptyMessage.Text = monitor?.WaveformStatusText ?? "等待设备连接并开始采集";
        emptyMessage.Visibility = Visibility.Visible;
        eraseBand.IsHidden = true;
        eraseWrapBand.IsHidden = true;
        eventMarkers.Children.Clear();
        lastMarkerLayoutSignature = null;
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

        var colors = monitor!.VisibleChannelColors;
        EnsureTraceSeries(labels.Select((label, index) => new WaveformDisplayTrace(label, [], colors[index])).ToArray());
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

    private bool UpdateEventMarkers(WaveformDisplayFrame? frame, LiveMonitoringViewModel? monitor)
    {
        if (frame is null || monitor is null || eventMarkers.ActualWidth <= 0 || eventMarkers.ActualHeight <= 0)
        {
            lastMarkerLayoutSignature = null;
            return false;
        }

        var pageStart = frame.WindowStartSampleCounter;
        var pageEndExclusive = checked(pageStart + frame.WindowSampleCount);
        var plotArea = WaveformPlotLayout.GetPlotArea(surface);
        if (plotArea.Width <= 0 || plotArea.Height <= 0)
        {
            lastMarkerLayoutSignature = null;
            return false;
        }

        var markerSignature = CreateMarkerLayoutSignature(frame, monitor, plotArea);
        if (StringComparer.Ordinal.Equals(markerSignature, lastMarkerLayoutSignature))
        {
            return false;
        }

        lastMarkerLayoutSignature = markerSignature;
        eventMarkers.Children.Clear();

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

        return true;
    }

    private static string CreateMarkerLayoutSignature(
        WaveformDisplayFrame frame,
        LiveMonitoringViewModel monitor,
        Rect plotArea)
    {
        var events = string.Join(';', monitor.LiveRecordingEvents.Select(item =>
            $"{item.Id}:{item.StartSample}:{item.DurationSamples}:{item.CoordinateStatus}:{item.DefinitionSnapshot.Color}"));
        var boundaries = string.Join(';', monitor.LiveLifecycleBoundaries.Select(boundary =>
            $"{boundary.Kind}:{boundary.SampleCounter}:{boundary.OccurredAtUtc.UtcTicks}"));
        return string.Join('|',
            frame.WindowStartSampleCounter,
            frame.WindowSampleCount,
            plotArea.Width.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            plotArea.Height.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            events,
            boundaries);
    }

    private void UpdateLifecycleBoundaries(WaveformDisplayFrame frame, LiveMonitoringViewModel? monitor)
    {
        foreach (var line in eventMarkers.Children.OfType<Line>().ToArray()) eventMarkers.Children.Remove(line);
        if (monitor is null) return;
        var plotArea = WaveformPlotLayout.GetPlotArea(surface);
        var pageStart = frame.WindowStartSampleCounter;
        var pageEnd = pageStart + frame.WindowSampleCount;
        foreach (var boundary in monitor.LiveLifecycleBoundaries)
        {
            var recordingFirst = monitor.RecordingFirstSampleCounter;
            if (recordingFirst is null) continue;
            var relative = boundary.SampleCounter - recordingFirst.Value;
            var display = monitor.ToDisplayCounterFromRecordingSample(relative);
            if (display is null || display < pageStart || display >= pageEnd) continue;
            var x = plotArea.Left + (display.Value - pageStart) / (double)frame.WindowSampleCount * plotArea.Width;
            eventMarkers.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = plotArea.Top, Y2 = plotArea.Bottom,
                Stroke = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                IsHitTestVisible = false,
            });
            WaveformEventTimeLabel.Add(
                eventMarkers,
                x,
                plotArea.Top + 3,
                RecordingClockLabelFormatter.FormatSampleMilliseconds(
                    monitor.RecordingStartUtc ?? DateTimeOffset.UtcNow,
                    relative,
                    monitor.SamplingRateHz),
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
            for (var index = 0; index < traces.Count; index++)
            {
                traceSeries[index].Renderable.Stroke = WaveformTraceColors.ToColor(traces[index].Color, index);
            }
            return;
        }

        activeLabels = labels;
        surface.RenderableSeries.Clear();
        traceSeries.Clear();
        for (var index = 0; index < traces.Count; index++)
        {
            var data = new XyDataSeries<double, double>();
            var renderable = new FastLineRenderableSeries
            {
                DataSeries = data,
                Stroke = WaveformTraceColors.ToColor(traces[index].Color, index),
                StrokeThickness = 1,
            };
            surface.RenderableSeries.Add(renderable);
            traceSeries.Add(new TraceSeries(renderable, data));
        }

        RebuildLabels(traces);
    }

    private static void UpdateSeries(
        TraceSeries series,
        WaveformDisplayFrame frame,
        WaveformDisplayTrace trace,
        int traceIndex,
        double displayScale)
    {
        var xValues = series.XValues;
        var yValues = series.YValues;
        xValues.Clear();
        yValues.Clear();
        xValues.EnsureCapacity(trace.Points.Count * 2);
        yValues.EnsureCapacity(trace.Points.Count * 2);

        var baseline = frame.Traces.Count - traceIndex - .5d;
        var hasPreviousPoint = false;
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

        var canUpdateYValues = series.LastXValues.Count == xValues.Count &&
            series.LastXValues.SequenceEqual(xValues);
        using (series.Data.SuspendUpdates())
        {
            if (canUpdateYValues)
            {
                for (var index = 0; index < yValues.Count; index++)
                {
                    if (!double.Equals(series.LastYValues[index], yValues[index]))
                    {
                        series.Data.Update(index, yValues[index]);
                    }
                }
            }
            else
            {
                series.Data.Clear();
                if (xValues.Count > 0)
                {
                    series.Data.Append(xValues, yValues);
                }
            }
        }

        series.LastXValues.Clear();
        series.LastXValues.AddRange(xValues);
        series.LastYValues.Clear();
        series.LastYValues.AddRange(yValues);
    }

    private void RebuildLabels(IReadOnlyList<WaveformDisplayTrace> traces)
    {
        channelLabels.Children.Clear();
        channelLabels.RowDefinitions.Clear();
        for (var index = 0; index < traces.Count; index++)
        {
            channelLabels.RowDefinitions.Add(new RowDefinition());
            var traceColor = WaveformTraceColors.ToColor(traces[index].Color, index);
            var channelLabel = new TextBlock
            {
                Text = traces[index].Label,
                Foreground = new SolidColorBrush(traceColor),
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0),
            };
            Grid.SetRow(channelLabel, index);
            channelLabels.Children.Add(channelLabel);
        }
    }

    private void SyncLabelPlotArea()
    {
        WaveformPlotLayout.SyncLabelPlotArea(surface, channelLabels, ref lastLabelPlotMargin);
    }

    private sealed class TraceSeries(FastLineRenderableSeries renderable, XyDataSeries<double, double> data)
    {
        public FastLineRenderableSeries Renderable { get; } = renderable;

        public XyDataSeries<double, double> Data { get; private set; } = data;

        public List<double> XValues { get; } = [];

        public List<double> YValues { get; } = [];

        public List<double> LastXValues { get; } = [];

        public List<double> LastYValues { get; } = [];

        public void ReplaceData(PreparedWaveformTrace prepared)
        {
            Data = prepared.Data;
            Renderable.DataSeries = prepared.Data;
            LastXValues.Clear();
            LastXValues.AddRange(prepared.XValues);
            LastYValues.Clear();
            LastYValues.AddRange(prepared.YValues);
        }
    }

}
