using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BrainPlatform.Desktop.ViewModels;
using SciChart.Charting;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Annotations;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Views;

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
    private double activeDisplayWindowSeconds;
    private int activeTraceCount;
    private Thickness? lastLabelPlotMargin;

    public LiveSciChartCanvas()
    {
        ConfigureSurface();
        Content = CreateLayout();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += (_, _) => RequestRefresh();
        SizeChanged += (_, _) => RequestRefresh();
        surface.LayoutUpdated += (_, _) => SyncLabelPlotArea();
    }

    private void ConfigureSurface()
    {
        surface.Background = Brushes.White;
        VisualXcceleratorEngine.SetIsEnabled(surface, true);

        xAxis.AutoRange = AutoRange.Never;
        xAxis.VisibleRange = new DoubleRange(0, 10);
        xAxis.AxisAlignment = AxisAlignment.Bottom;
        xAxis.DrawLabels = true;
        xAxis.DrawMajorBands = false;
        xAxis.DrawMinorGridLines = false;
        xAxis.DrawMajorGridLines = true;
        xAxis.DrawMajorTicks = false;
        xAxis.DrawMinorTicks = false;
        xAxis.AutoTicks = false;
        xAxis.MajorDelta = 1d;
        xAxis.MinorDelta = 0.5d;
        xAxis.TextFormatting = "0";
        xAxis.LabelProvider = sweepTimeLabels;
        xAxis.TickTextBrush = new SolidColorBrush(Color.FromRgb(49, 77, 126));
        xAxis.MajorGridLineStyle = new Style(typeof(Line))
        {
            Setters = { new Setter(Shape.StrokeProperty, new SolidColorBrush(Color.FromRgb(226, 232, 240))) },
        };

        yAxis.AutoRange = AutoRange.Never;
        yAxis.VisibleRange = new DoubleRange(0, 1);
        // The Y axis has no labels or ticks in the EEG stacked-trace view.
        // Keeping its layout slot at the left creates a visible blank gutter
        // between montage labels and the first waveform sample. Put that
        // invisible slot at the far edge instead.
        yAxis.AxisAlignment = AxisAlignment.Right;
        yAxis.DrawLabels = false;
        yAxis.DrawMajorBands = false;
        yAxis.DrawMinorGridLines = false;
        yAxis.DrawMajorGridLines = false;
        yAxis.DrawMajorTicks = false;
        yAxis.DrawMinorTicks = false;

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
        Grid.SetColumn(surface, 1);
        Grid.SetColumn(emptyMessage, 1);
        layout.Children.Add(channelLabels);
        layout.Children.Add(surface);
        layout.Children.Add(emptyMessage);
        return layout;
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
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
        refreshTimer?.Stop();
        refreshTimer = null;
        eraseAnimationTimer?.Stop();
        eraseAnimationTimer = null;
        eraseBandAnimator.Reset();
        frameWorker?.Dispose();
        frameWorker = null;
        lastRequestedRevision = null;
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
        var plotHeight = GetPlotArea().Height;
        var displayWindowSeconds = monitor.GetDisplayWindowSeconds(viewportWidthDips);
        var revision = WaveformRenderRevision.Create(
            source,
            displayWindowSeconds,
            horizontalPixels,
            monitor.SensitivityMicrovoltsPerMillimeter,
            plotHeight);
        if (revision == lastRequestedRevision)
        {
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
            ShowEmptyState(DataContext as LiveMonitoringViewModel);
            return;
        }

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
        sweepTimeLabels.Update(frame.PageStartElapsedSeconds, frame.CursorSeconds);
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
        var pixelsPerMillimeter = VisualTreeHelper.GetDpi(surface).PixelsPerInchY / 25.4;
        var displayScale = frame.Traces.Count * pixelsPerMillimeter /
            (result.Request.PlotHeight * result.Request.SensitivityMicrovoltsPerMillimeter);

        using (surface.SuspendUpdates())
        {
            for (var index = 0; index < frame.Traces.Count; index++)
            {
                UpdateSeries(traceSeries[index], frame, frame.Traces[index], index, displayScale);
            }
        }
    }

    private void ShowEmptyState(LiveMonitoringViewModel? monitor)
    {
        emptyMessage.Text = monitor?.WaveformStatusText ?? "等待设备连接并开始采集";
        emptyMessage.Visibility = Visibility.Visible;
        eraseBand.IsHidden = true;
        eraseWrapBand.IsHidden = true;
        activeDisplayWindowSeconds = 0;
        activeTraceCount = 0;
        eraseBandAnimator.Reset();
        sweepTimeLabels.Update(0, 0);
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

    private Rect GetPlotArea()
    {
        if (surface.GridLinesPanel is not FrameworkElement gridLines ||
            gridLines.ActualWidth <= 0 ||
            gridLines.ActualHeight <= 0)
        {
            return new Rect(0, 0, Math.Max(1, surface.ActualWidth), Math.Max(1, surface.ActualHeight));
        }

        var origin = gridLines.TranslatePoint(new Point(), surface);
        return new Rect(origin.X, origin.Y, gridLines.ActualWidth, gridLines.ActualHeight);
    }

    private void SyncLabelPlotArea()
    {
        var plotArea = GetPlotArea();
        if (surface.ActualHeight <= 0 || plotArea.Height <= 0)
        {
            return;
        }

        var margin = new Thickness(
            0,
            Math.Max(0, plotArea.Top),
            0,
            Math.Max(0, surface.ActualHeight - plotArea.Bottom));
        if (lastLabelPlotMargin is { } previous &&
            Math.Abs(previous.Top - margin.Top) < 0.1 &&
            Math.Abs(previous.Bottom - margin.Bottom) < 0.1)
        {
            return;
        }

        lastLabelPlotMargin = margin;
        channelLabels.Margin = margin;
    }

    private sealed class TraceSeries(XyDataSeries<double, double> data)
    {
        public XyDataSeries<double, double> Data { get; } = data;

        public List<double> XValues { get; } = [];

        public List<double> YValues { get; } = [];
    }

}
