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
    private readonly ReviewSampleMarkerLayer sampleMarkerLayer;
    private readonly WallClockSecondTickProvider wallClockTicks = new();
    private readonly NumericAxis yAxis = new();
    private readonly Grid labels = new();
    private readonly Grid chartHost = new();
    private readonly Grid rootLayout = WaveformPlotLayout.CreateRootGrid();
    private readonly ReviewEventMarkerLayer eventMarkers;
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
    private CancellationTokenSource? displayFrameLoad;
    private long displayFrameRequest;
    private RecordingReviewFrame? displayAttemptFrame;
    private double displayAttemptViewportStart = double.NaN;
    private double displayAttemptVisibleDuration = double.NaN;
    private int displayAttemptWidth;
    private ScreenScaleContext screenScale = ScreenScaleContext.Nominal;
    private RecordingReviewFrame? renderedFrame;
    private ReviewDisplayFrame? renderedDisplayFrame;
    private double renderedPlotHeight;
    private double renderedSensitivity;
    private double renderedMillimetersPerDip;
    private bool isPointLineModeEnabled;

    public bool IsPointLineModeEnabled
    {
        get => isPointLineModeEnabled;
        set
        {
            if (isPointLineModeEnabled == value) return;
            isPointLineModeEnabled = value;
            RefreshSampleMarkers(renderedFrame, DataContext as RecordingReviewViewModel);
        }
    }

    public RecordingReviewSciChartCanvas()
    {
        ConfigureSurface();
        sampleMarkerLayer = new ReviewSampleMarkerLayer(surface);
        eventMarkers = new ReviewEventMarkerLayer(surface, chartHost);
        var layout = rootLayout;
        labels.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(labels, 0);
        chartHost.Children.Add(surface);
        chartHost.Children.Add(emptyMessage);
        chartHost.Children.Add(eventMarkers.Canvas);
        chartHost.Children.Add(playbackCursor);
        chartHost.SizeChanged += (_, _) =>
        {
            var viewModel = DataContext as RecordingReviewViewModel;
            viewModel?.UpdateViewportWidth(chartHost.ActualWidth);
            ApplyFrame(viewModel?.CurrentFrame, viewModel);
            RefreshSampleMarkers(renderedFrame, viewModel);
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
        displayFrameLoad?.Cancel();
        displayFrameLoad?.Dispose();
        displayFrameLoad = null;
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
            eventMarkers.Rebuild(sender as RecordingReviewViewModel);
        }
        else if (e.PropertyName == nameof(RecordingReviewViewModel.TimeDisplay))
        {
            UpdateTimePresentation(sender as RecordingReviewViewModel);
            eventMarkers.Rebuild(sender as RecordingReviewViewModel);
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
            var viewModel = sender as RecordingReviewViewModel;
            UpdateVisibleRange(viewModel);
            UpdatePlaybackCursor(viewModel);
            eventMarkers.Update(viewModel);
            RefreshSampleMarkers(renderedFrame, viewModel);
            // Playback moves the axis every render tick. Do not cancel and
            // restart an optional display-level read for each tick; the
            // bounded source frame remains the authoritative smooth path.
            if (viewModel?.IsPlaying != true)
                StartDisplayFrameLoad(viewModel);
        }
        else if (e.PropertyName == nameof(RecordingReviewViewModel.IsPlaying) &&
                 (sender as RecordingReviewViewModel)?.IsPlaying != true)
        {
            StartDisplayFrameLoad(sender as RecordingReviewViewModel);
        }
    }

    private void ApplyFrame(RecordingReviewFrame? frame, RecordingReviewViewModel? viewModel)
    {
        UpdateTimePresentation(viewModel);
        if (!ReferenceEquals(renderedFrame, frame))
        {
            renderedDisplayFrame = null;
            CancelDisplayFrameLoad();
        }
        if (frame is null)
        {
            emptyMessage.Text = viewModel?.StatusText ?? "等待加载回溯数据";
            emptyMessage.Visibility = Visibility.Visible;
            ClearSeries();
            renderedFrame = null;
            renderedDisplayFrame = null;
            CancelDisplayFrameLoad();
            eventMarkers.Clear();
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

        RefreshSampleMarkers(frame, viewModel);
        UpdatePlaybackCursor(viewModel);
        eventMarkers.Rebuild(viewModel);
        if (renderedDisplayFrame is { } display &&
            ReferenceEquals(displayAttemptFrame, frame) &&
            Math.Abs(display.WindowStartSeconds - (viewModel?.ViewportStartSeconds ?? double.NaN)) < 0.000_001)
            RenderDisplayFrame(display, viewModel!);
        StartDisplayFrameLoad(viewModel);
    }

    private void StartDisplayFrameLoad(RecordingReviewViewModel? viewModel)
    {
        if (viewModel?.CurrentFrame is not { } frame || chartHost.ActualWidth <= 0 ||
            string.IsNullOrWhiteSpace(frame.DisplaySourceIdentity))
            return;

        var width = Math.Max(1, (int)Math.Round(chartHost.ActualWidth));
        if (ReferenceEquals(displayAttemptFrame, frame) &&
            Math.Abs(displayAttemptViewportStart - viewModel.ViewportStartSeconds) < 0.000_001 &&
            Math.Abs(displayAttemptVisibleDuration - viewModel.VisibleDurationSeconds) < 0.000_001 &&
            displayAttemptWidth == width)
            return;

        var request = Interlocked.Increment(ref displayFrameRequest);
        displayFrameLoad?.Cancel();
        displayFrameLoad?.Dispose();
        var load = new CancellationTokenSource();
        displayFrameLoad = load;
        displayAttemptFrame = frame;
        displayAttemptViewportStart = viewModel.ViewportStartSeconds;
        displayAttemptVisibleDuration = viewModel.VisibleDurationSeconds;
        displayAttemptWidth = width;
        _ = LoadDisplayFrameAsync(viewModel, frame, request, load);
    }

    private async Task LoadDisplayFrameAsync(
        RecordingReviewViewModel viewModel,
        RecordingReviewFrame frame,
        long request,
        CancellationTokenSource load)
    {
        try
        {
            var display = await viewModel.TryBuildDisplayFrameAsync(
                frame,
                displayAttemptWidth,
                load.Token);
            if (load.IsCancellationRequested || request != Volatile.Read(ref displayFrameRequest) ||
                !ReferenceEquals(DataContext, viewModel) || !ReferenceEquals(viewModel.CurrentFrame, frame))
                return;
            if (display is null)
            {
                renderedDisplayFrame = null;
                return;
            }

            renderedDisplayFrame = display;
            RenderDisplayFrame(display, viewModel);
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested) { }
        catch { /* A display cache miss must never break the raw review path. */ }
        finally
        {
            if (ReferenceEquals(displayFrameLoad, load))
            {
                displayFrameLoad = null;
                load.Dispose();
            }
        }
    }

    private void CancelDisplayFrameLoad()
    {
        Interlocked.Increment(ref displayFrameRequest);
        displayFrameLoad?.Cancel();
        displayFrameLoad?.Dispose();
        displayFrameLoad = null;
        displayAttemptFrame = null;
        displayAttemptViewportStart = double.NaN;
        displayAttemptVisibleDuration = double.NaN;
        displayAttemptWidth = 0;
    }

    private void RenderDisplayFrame(ReviewDisplayFrame frame, RecordingReviewViewModel viewModel)
    {
        var plotHeight = WaveformPlotLayout.GetPlotArea(surface).Height;
        var sensitivity = viewModel.SensitivityMicrovoltsPerMillimeter;
        var displayScale = ScreenScaleCalculator.VerticalDisplayScale(
            frame.OutputChannelNames.Count,
            plotHeight,
            sensitivity,
            screenScale.MillimetersPerDipY);
        using (surface.SuspendUpdates())
        {
            for (var traceIndex = 0; traceIndex < traceSeries.Count && traceIndex < frame.Traces.Count; traceIndex++)
            {
                var baseline = frame.OutputChannelNames.Count - traceIndex - 0.5;
                var trace = frame.Traces[traceIndex];
                var y = trace.ValuesVolts.Select(value =>
                    double.IsFinite(value) ? baseline + value * 1_000_000d * displayScale : double.NaN).ToArray();
                using (traceSeries[traceIndex].Data.SuspendUpdates())
                {
                    traceSeries[traceIndex].Data.Clear();
                    traceSeries[traceIndex].Data.Append(trace.XSeconds.ToArray(), y);
                }
            }
        }
    }

    private void RefreshSampleMarkers(RecordingReviewFrame? frame, RecordingReviewViewModel? viewModel)
    {
        sampleMarkerLayer.Refresh(
            frame, viewModel, xAxis.VisibleRange as DoubleRange,
            WaveformPlotLayout.GetPlotArea(surface), screenScale, IsPointLineModeEnabled);
    }

    private void EnsureTraceSeries(IReadOnlyList<string> names, IReadOnlyList<string>? colors)
    {
        if (activeLabels.SequenceEqual(names))
        {
            for (var index = 0; index < names.Count; index++)
            {
                var traceColor = WaveformTraceColors.ToColor(colors?.ElementAtOrDefault(index), index);
                traceSeries[index].LineSeries.Stroke = traceColor;
            }
            sampleMarkerLayer.UpdateColors(names, colors);
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
            var lineSeries = new FastLineRenderableSeries
            {
                DataSeries = data,
                Stroke = traceColor,
                StrokeThickness = 1,
                DrawNaNAs = LineDrawMode.Gaps,
            };
            surface.RenderableSeries.Add(lineSeries);
            traceSeries.Add(new TraceSeries(data, lineSeries));
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
        sampleMarkerLayer.EnsureSeries(names, colors);
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
        sampleMarkerLayer.Clear();
        foreach (var series in traceSeries)
        {
            series.Data.Clear();
        }
    }

    private void SyncLabelPlotArea()
    {
        WaveformPlotLayout.SyncLabelPlotArea(surface, labels, ref lastLabelPlotMargin);
    }

    private sealed record TraceSeries(
        XyDataSeries<double, double> Data,
        FastLineRenderableSeries LineSeries);
}
