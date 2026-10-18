using System.Diagnostics;
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
using BrainPlatform.Desktop.Modules.Review.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

public sealed partial class RecordingReviewSciChartCanvas : UserControl
{
    private const int MaximumPointsPerTrace = 2_000;
    private readonly SciChartSurface surface = new();
    private readonly NumericAxis xAxis = new();
    private readonly RecordingTimeLabelProvider recordingTimeLabels = new();
    private readonly WallClockSecondTickProvider wallClockTicks = new();
    private readonly NumericAxis yAxis = new();
    private readonly Grid labels = new();
    private readonly Grid chartHost = new();
    private readonly Grid rootLayout = WaveformPlotLayout.CreateRootGrid();
    private readonly Canvas eventMarkers = new();
    private readonly ReviewPlaybackCursor playbackCursor = new();
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
    private RecordingReviewFrame? renderedFrame;
    private double renderedPlotHeight;
    private double renderedSensitivity;
    private double renderedMillimetersPerDip;

    public RecordingReviewSciChartCanvas()
    {
        ConfigureSurface();
        var layout = rootLayout;
        labels.HorizontalAlignment = HorizontalAlignment.Stretch;
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
        Grid.SetColumn(chartHost, 2);
        chartHost.Margin = new Thickness(0);
        layout.Children.Add(labels);
        layout.Children.Add(chartHost);
        Content = layout;
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
        surface.LayoutUpdated += (_, _) =>
        {
            SyncLabelPlotArea();
            UpdatePlaybackCursor(DataContext as RecordingReviewViewModel);
        };
    }

    private void ConfigureSurface()
    {
        surface.Background = Brushes.White;
        VisualXcceleratorEngine.SetIsEnabled(surface, true);
        WaveformAxisPolicy.ConfigureHorizontalAxis(xAxis, recordingTimeLabels);
        xAxis.TickProvider = wallClockTicks;
        xAxis.TickTextBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105));
        xAxis.FontSize = 11;
        xAxis.MajorGridLineStyle = new Style(typeof(Line))
        {
            Setters = { new Setter(Shape.StrokeProperty, new SolidColorBrush(Color.FromRgb(226, 232, 240))) },
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
            or nameof(RecordingReviewViewModel.IsLoading)
            or nameof(RecordingReviewViewModel.StatusText))
        {
            ApplyFrame((sender as RecordingReviewViewModel)?.CurrentFrame, sender as RecordingReviewViewModel);
        }
        else if (e.PropertyName == nameof(RecordingReviewViewModel.RecordingEvents))
        {
            UpdateEventMarkers(sender as RecordingReviewViewModel);
            UpdateLifecycleBoundaries(sender as RecordingReviewViewModel);
        }
        else if (e.PropertyName == nameof(RecordingReviewViewModel.TimeDisplay))
        {
            UpdateTimePresentation(sender as RecordingReviewViewModel);
            UpdateEventMarkers(sender as RecordingReviewViewModel);
            UpdateLifecycleBoundaries(sender as RecordingReviewViewModel);
        }
        else if (e.PropertyName == nameof(RecordingReviewViewModel.PositionSeconds))
        {
            // Drag previews can update PositionSeconds before a cold frame
            // finishes. Keep the axis and cursor on that same target so the
            // bottom time readout cannot outrun the chart coordinate system.
            // This only changes the visible range; trace arrays are reused.
            UpdateVisibleRange(sender as RecordingReviewViewModel);
            UpdatePlaybackCursor(sender as RecordingReviewViewModel);
        }
        else if (e.PropertyName == nameof(RecordingReviewViewModel.ViewportStartSeconds))
        {
            UpdateVisibleRange(sender as RecordingReviewViewModel);
            UpdatePlaybackCursor(sender as RecordingReviewViewModel);
            UpdateEventMarkers(sender as RecordingReviewViewModel);
            UpdateLifecycleBoundaries(sender as RecordingReviewViewModel);
        }
    }

    private void ApplyFrame(RecordingReviewFrame? frame, RecordingReviewViewModel? viewModel)
    {
        UpdateTimePresentation(viewModel);
        if (frame is null)
        {
            emptyMessage.Text = viewModel?.StatusText ?? "等待加载回溯数据";
            emptyMessage.Visibility = Visibility.Visible;
            ClearSeries();
            renderedFrame = null;
            eventMarkers.Children.Clear();
            playbackCursor.Visibility = Visibility.Collapsed;
            return;
        }

        var names = frame.OutputChannelNames.ToArray();
        EnsureTraceSeries(names, frame.OutputChannelColors);
        UpdateVisibleRange(viewModel);
        xAxis.InvalidateElement();
        yAxis.VisibleRange = new DoubleRange(0, Math.Max(1, names.Length));
        SyncLabelPlotArea();
        emptyMessage.Text = viewModel?.StatusText ?? "已加载";
        emptyMessage.Visibility = viewModel?.IsLoading == true || frame.Segments.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var plotHeight = WaveformPlotLayout.GetPlotArea(surface).Height;
        var sensitivity = viewModel?.SensitivityMicrovoltsPerMillimeter ?? 10;
        if (!ReferenceEquals(renderedFrame, frame) || renderedPlotHeight != plotHeight ||
            renderedSensitivity != sensitivity || renderedMillimetersPerDip != screenScale.MillimetersPerDipY)
        {
            using (surface.SuspendUpdates())
            {
                for (var traceIndex = 0; traceIndex < traceSeries.Count; traceIndex++)
                    UpdateSeries(traceSeries[traceIndex], frame, traceIndex, names.Length);
            }
            renderedFrame = frame;
            renderedPlotHeight = plotHeight;
            renderedSensitivity = sensitivity;
            renderedMillimetersPerDip = screenScale.MillimetersPerDipY;
        }

        UpdatePlaybackCursor(viewModel);
        UpdateEventMarkers(viewModel);
        UpdateLifecycleBoundaries(viewModel);
    }

    private void EnsureTraceSeries(IReadOnlyList<string> names, IReadOnlyList<string>? colors)
    {
        if (activeLabels.SequenceEqual(names))
        {
            for (var index = 0; index < names.Count; index++)
            {
                surface.RenderableSeries[index].Stroke = WaveformTraceColors.ToColor(colors?.ElementAtOrDefault(index), index);
            }
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
            var traceColor = WaveformTraceColors.ToColor(colors?.ElementAtOrDefault(index), index);
            surface.RenderableSeries.Add(new FastLineRenderableSeries
            {
                DataSeries = data,
                Stroke = traceColor,
                StrokeThickness = 1,
                DrawNaNAs = LineDrawMode.Gaps,
            });
            traceSeries.Add(new TraceSeries(data));
            labels.RowDefinitions.Add(new RowDefinition());
            var label = new TextBlock
            {
                Text = names[index],
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(traceColor),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0),
            };
            Grid.SetRow(label, index);
            labels.Children.Add(label);
        }
    }

    private void UpdateSeries(TraceSeries series, RecordingReviewFrame frame, int traceIndex, int traceCount)
    {
        var started = ReviewPerformanceDiagnostics.Enabled ? Stopwatch.GetTimestamp() : 0L;
        var baseline = traceCount - traceIndex - 0.5;
        // The plot is laid out in DIP, so sensitivity must use the calibrated
        // physical DIP/mm conversion, not the monitor's nominal raster DPI.
        var plotHeight = WaveformPlotLayout.GetPlotArea(surface).Height;
        var sensitivity = (DataContext as RecordingReviewViewModel)?.SensitivityMicrovoltsPerMillimeter ?? 10;
        var displayScale = ScreenScaleCalculator.VerticalDisplayScale(
            traceCount,
            plotHeight,
            sensitivity,
            screenScale.MillimetersPerDipY);
        var points = ReviewWaveformTraceProjection.Build(frame, traceIndex, baseline, displayScale, MaximumPointsPerTrace);

        using (series.Data.SuspendUpdates())
        {
            series.Data.Clear();
            if (points.X.Count > 0)
            {
                series.Data.Append(points.X, points.Y);
            }
        }

        if (ReviewPerformanceDiagnostics.Enabled)
        {
            ReviewPerformanceDiagnostics.Default.Record(new ReviewPerformanceSample(
                (DataContext as RecordingReviewViewModel)?.ReviewSessionId ?? Guid.Empty,
                frame.RecordingSessionId,
                ReviewPerformanceStage.ChartBinding,
                ReviewPerformanceSource.PublishedFrame,
                frame.Revision,
                frame.WindowStartSeconds,
                frame.WindowEndSeconds,
                traceCount,
                points.X.Count,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        }
    }

    private void UpdatePlaybackCursor(RecordingReviewViewModel? viewModel)
    {
        if (viewModel?.CurrentFrame is null || chartHost.ActualWidth <= 0)
        {
            playbackCursor.Visibility = Visibility.Collapsed;
            return;
        }

        if (xAxis.VisibleRange is not DoubleRange visibleRange) return;
        var visibleStart = visibleRange.Min;
        var visibleEnd = visibleRange.Max;
        var duration = Math.Max(0.001, visibleEnd - visibleStart);
        var fraction = Math.Clamp((viewModel.PositionSeconds - visibleStart) / duration, 0, 1);
        var plotArea = WaveformPlotLayout.GetPlotArea(surface);
        var plotOrigin = surface.TranslatePoint(plotArea.TopLeft, chartHost);
        playbackCursor.SetPosition(new Rect(plotOrigin, plotArea.Size), fraction);
        // Keep the cursor visible while a cold target is loading. Its position
        // is calculated against the requested visible axis, so rapid
        // bidirectional drags do not leave it stranded at an old edge.
        playbackCursor.Visibility = Visibility.Visible;
    }

    private void UpdateEventMarkers(RecordingReviewViewModel? viewModel)
    {
        eventMarkers.Children.Clear();
        if (viewModel is null || chartHost.ActualWidth <= 0 || chartHost.ActualHeight <= 0)
            return;

        var start = viewModel.ViewportStartSeconds;
        var end = Math.Min(viewModel.DurationSeconds, start + viewModel.VisibleDurationSeconds);
        var duration = Math.Max(0.001, end - start);
        var plotArea = WaveformPlotLayout.GetPlotArea(surface);
        var plotOrigin = surface.TranslatePoint(plotArea.TopLeft, chartHost);
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
            var left = plotOrigin.X + Math.Clamp((itemStart - start) / duration, 0, 1) * plotArea.Width;
            var right = plotOrigin.X + Math.Clamp((itemEnd - start) / duration, 0, 1) * plotArea.Width;
            var color = ParseColor(item.DefinitionSnapshot.Color);
            eventMarkers.Children.Add(new Border
            {
                ToolTip = RecordingEventDisplayTime.FormatMarker(item, viewModel.RecordingStartUtc, viewModel.SamplingRateHz),
                IsHitTestVisible = !item.IsInterval,
                Width = item.IsInterval ? Math.Max(2, right - left) : 2,
                Height = plotArea.Height,
                Background = item.IsInterval
                    ? new SolidColorBrush(Color.FromArgb(32, color.R, color.G, color.B))
                    : new SolidColorBrush(color),
                BorderBrush = item.IsInterval ? new SolidColorBrush(Color.FromArgb(150, color.R, color.G, color.B)) : null,
                BorderThickness = item.IsInterval ? new Thickness(1, 0, 1, 0) : new Thickness(0),
                Margin = new Thickness(left, plotOrigin.Y, 0, 0),
            });
            WaveformEventTimeLabel.Add(
                eventMarkers,
                left,
                plotOrigin.Y + 3,
                viewModel.TimeDisplay.FormatPoint(itemStart),
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
                viewModel.TimeDisplay.FormatPoint(seconds),
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
        // The requested viewport is authoritative during navigation. A cold
        // target may leave the plot temporarily empty, but keeping the old
        // axis made the cursor stick to an edge while the bottom time display
        // had already moved to the new target. That was especially visible
        // during rapid bidirectional drags. The complete frame is still
        // published atomically when it arrives.
        if (xAxis.VisibleRange is not DoubleRange range || range.Min != start || range.Max != end)
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
