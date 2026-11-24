using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Common.Extensions;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Annotations;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.Axes.LabelProviders;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Review.Views;

/// <summary>One persistent PSD series and frequency annotations; science stays in backend projections.</summary>
public sealed partial class ReviewCustomFrequencyChart : UserControl
{
    private readonly SciChartSurface surface = new()
    {
        Background = Brushes.White, ShowLicensingWarnings = false,
        Padding = new Thickness(0, 28, 48, 0), ClipOverlayAnnotations = false,
    };
    private readonly XyDataSeries<double, double> values = new();
    private readonly NumericAxis xAxis = new()
    {
        AxisTitle = "频率 (Hz)", AutoRange = AutoRange.Never, VisibleRange = new DoubleRange(0, 50),
        TextFormatting = "0.#", CursorTextFormatting = "0.### 'Hz'",
        DrawMajorBands = false, DrawMinorGridLines = false,
    };
    private readonly NumericAxis yAxis = new()
    {
        AxisTitle = "PSD (µV²/Hz)", AxisAlignment = AxisAlignment.Left, AutoRange = AutoRange.Never,
        VisibleRange = new DoubleRange(0, 1), TextFormatting = "G3", CursorTextFormatting = "G6",
        DrawMajorBands = false, DrawMinorGridLines = false,
    };
    private static readonly SolidColorBrush PrimaryBlueBrush = new(Color.FromRgb(29, 103, 233)); // #1D67E9
    private static readonly SolidColorBrush BoundaryBlueBrush = new(Color.FromRgb(20, 105, 238)); // #1469EE
    private static readonly SolidColorBrush SelectionFillBrush = new(Color.FromArgb(36, 51, 119, 255)); // #3377FF 14% opacity

    private readonly VerticalLineAnnotation pointCursor = CreateCursor(PrimaryBlueBrush);
    private readonly VerticalLineAnnotation lowCursor = CreateCursor(BoundaryBlueBrush);
    private readonly VerticalLineAnnotation highCursor = CreateCursor(BoundaryBlueBrush);
    private readonly BoxAnnotation range = new()
    {
        Background = SelectionFillBrush,
        BorderThickness = new Thickness(0), IsEditable = false, IsHitTestVisible = false,
        AnnotationCanvas = AnnotationCanvas.BelowChart,
    };
    private readonly TextBlock empty = new()
    {
        Text = "当前通道暂无可用 PSD", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false, Margin = new Thickness(20),
    };
    private ReviewCustomFrequencyInspectionViewModel? observedModel;
    private double maximumDensity = 1;
    private long? renderPendingAt;
    private string? renderWindowId;

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(ReviewCustomFrequencyInspectionViewModel), typeof(ReviewCustomFrequencyChart),
        new PropertyMetadata(null, (owner, _) => ((ReviewCustomFrequencyChart)owner).ObserveModel()));

    public ReviewCustomFrequencyInspectionViewModel? Model
    {
        get => (ReviewCustomFrequencyInspectionViewModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public ReviewCustomFrequencyChart()
    {
        SciChart.Charting.ThemeManager.SetTheme(surface, "BrightSpark");
        xAxis.LabelProvider = new FrequencyCursorLabelProvider(values);
        surface.XAxes.Add(xAxis);
        surface.YAxes.Add(yAxis);
        var series = new FastMountainRenderableSeries
        {
            DataSeries = values,
            Stroke = Color.FromRgb(18, 101, 239), // #1265EF
            StrokeThickness = 2,
            ZeroLineY = 0,
            Fill = new LinearGradientBrush(
                Color.FromArgb(48, 52, 125, 243),
                Color.FromArgb(6, 52, 125, 243),
                90),
            DrawNaNAs = LineDrawMode.Gaps,
        };
        CursorModifier.SetSnapToSeries(series, true);
        surface.RenderableSeries.Add(series);
        surface.ChartModifier = new CursorModifier
        {
            IsEnabled = true, ShowAxisLabels = true, ShowTooltip = true,
            SnappingMode = CursorSnappingMode.CrosshairToSeriesX, UseInterpolation = false,
            ShowTooltipOn = ShowTooltipOptions.MouseOver,
            ReceiveHandledEvents = true,
        };
        surface.Annotations.Add(range);
        surface.Annotations.Add(pointCursor);
        surface.Annotations.Add(lowCursor);
        surface.Annotations.Add(highCursor);
        foreach (var cursor in new[] { pointCursor, lowCursor, highCursor })
            cursor.AnnotationLabels[0].SizeChanged += (_, _) => PositionFrequencyLabels();
        surface.SizeChanged += (_, _) => PositionFrequencyLabels();
        surface.PreviewMouseLeftButtonDown += OnSelectionMouseDown;
        surface.PreviewMouseMove += OnSelectionMouseMove;
        surface.PreviewMouseLeftButtonUp += OnSelectionMouseUp;
        surface.LostMouseCapture += (_, _) => EndSelectionDrag();
        surface.MouseLeave += (_, _) => { if (dragTarget == DragTarget.None) surface.ClearValue(CursorProperty); };
        var root = new Grid();
        root.Children.Add(surface);
        root.Children.Add(empty);
        Content = root;
        Loaded += (_, _) => ObserveModel();
        Unloaded += (_, _) => DetachModel();
        UpdateSelection();
    }

    private static VerticalLineAnnotation CreateCursor(Brush stroke)
    {
        var cursor = new VerticalLineAnnotation
        {
            Stroke = stroke, StrokeThickness = 2, IsEditable = false, ShowLabel = false,
            StrokeDashArray = [4, 3],
            LabelTextFormatting = "0.### 'Hz'", Cursor = Cursors.SizeWE,
        };
        cursor.AnnotationLabels.Add(new AnnotationLabel
        {
            LabelPlacement = LabelPlacement.Top, Background = stroke, Foreground = Brushes.White,
            FontSize = 11, FontWeight = FontWeights.SemiBold, Padding = new Thickness(8, 3, 8, 3),
            CornerRadius = new CornerRadius(4), RenderTransform = new TranslateTransform(0, -24),
        });
        cursor.AnnotationLabels.Add(new AnnotationLabel
        {
            Text = string.Empty, LabelPlacement = LabelPlacement.Bottom, Background = stroke,
            BorderBrush = Brushes.White, BorderThickness = new Thickness(2),
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
            RenderTransform = new TranslateTransform(0, 4),
        });
        return cursor;
    }

    private void ObserveModel()
    {
        DetachModel();
        if (!IsLoaded) return;
        observedModel = Model;
        if (observedModel is not null) observedModel.PropertyChanged += OnModelChanged;
        UpdateSeries();
        UpdateSelection();
    }

    private void DetachModel()
    {
        EndSelectionDrag();
        CompositionTarget.Rendering -= OnNextRender;
        renderPendingAt = null;
        if (observedModel is not null) observedModel.PropertyChanged -= OnModelChanged;
        observedModel = null;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReviewCustomFrequencyInspectionViewModel.Mode) or
            nameof(ReviewCustomFrequencyInspectionViewModel.FrequenciesHz) or
            nameof(ReviewCustomFrequencyInspectionViewModel.HasBins)) EndSelectionDrag();
        if (e.PropertyName == nameof(ReviewCustomFrequencyInspectionViewModel.PsdCurve)) UpdateSeries();
        if (e.PropertyName is nameof(ReviewCustomFrequencyInspectionViewModel.Mode) or
            nameof(ReviewCustomFrequencyInspectionViewModel.ActualFrequencyHz) or
            nameof(ReviewCustomFrequencyInspectionViewModel.RangeLowHz) or
            nameof(ReviewCustomFrequencyInspectionViewModel.RangeHighHz) or
            nameof(ReviewCustomFrequencyInspectionViewModel.HasBins)) UpdateSelection();
    }

    private void UpdateSeries()
    {
        var started = Stopwatch.GetTimestamp();
        var curve = Model?.PsdCurve ?? [];
        using (surface.SuspendUpdates())
        {
            values.Clear();
            values.SeriesName = $"{Model?.SelectedChannel} · PSD (µV²/Hz)";
            values.Append(curve.Select(point => point.FrequencyHz),
                curve.Select(point => point.DensityUv2PerHz ?? double.NaN));
            var available = curve.Where(point => point.DensityUv2PerHz.HasValue).ToArray();
            maximumDensity = available.Length == 0 ? 1 : Math.Max(1e-12, available.Max(point => point.DensityUv2PerHz!.Value) * 1.12);
            yAxis.VisibleRange = new DoubleRange(0, maximumDensity);
            empty.Visibility = available.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            surface.ChartModifier.IsEnabled = available.Length > 0;
        }
        UpdateSelection();
        if (DesktopLog.PerformanceDiagnosticsEnabled)
        {
            DesktopLog.Info($"review_custom_frequency_chart_perf psd_window_id={Model?.PsdWindowId ?? "none"}" +
                $" channel={Model?.SelectedChannel ?? "none"} points={curve.Count}" +
                $" series_update_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.000}");
            if (IsLoaded)
            {
                if (renderPendingAt is null) CompositionTarget.Rendering += OnNextRender;
                renderPendingAt = started;
                renderWindowId = Model?.PsdWindowId;
            }
        }
    }

    private sealed class FrequencyCursorLabelProvider(XyDataSeries<double, double> series) : NumericLabelProvider
    {
        public override string FormatCursorLabel(IComparable dataValue)
        {
            if (series.Count == 0 || !double.IsFinite(Convert.ToDouble(dataValue))) return string.Empty;
            // SciChart snaps the crosshair, but formats axis labels from the raw pointer X.
            var index = series.FindIndex(dataValue, SearchMode.Nearest);
            return index < 0 ? string.Empty : $"{series.XValues[index]:0.###} Hz";
        }
    }

    private void OnNextRender(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnNextRender;
        if (renderPendingAt is not { } started) return;
        renderPendingAt = null;
        DesktopLog.Info($"review_custom_frequency_render_callback_perf psd_window_id={renderWindowId ?? "none"}" +
            $" next_render_callback_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.000}");
    }

    private void UpdateSelection()
    {
        var model = Model;
        var single = model?.HasBins == true && model.Mode == ReviewCustomFrequencyMode.SingleFrequency;
        var interval = model?.HasBins == true && model.Mode == ReviewCustomFrequencyMode.Range;
        pointCursor.IsHidden = !single || model?.ActualFrequencyHz is null;
        lowCursor.IsHidden = highCursor.IsHidden = !interval;
        range.IsHidden = !interval || model?.IsRangeValid != true;
        if (model is null) return;
        if (model.ActualFrequencyHz is { } frequency) pointCursor.X1 = frequency;
        lowCursor.IsHidden |= !double.IsFinite(model.RangeLowHz);
        highCursor.IsHidden |= !double.IsFinite(model.RangeHighHz);
        if (double.IsFinite(model.RangeLowHz)) lowCursor.X1 = model.RangeLowHz;
        if (double.IsFinite(model.RangeHighHz)) highCursor.X1 = model.RangeHighHz;
        if (!range.IsHidden)
        {
            range.X1 = model.RangeLowHz;
            range.X2 = model.RangeHighHz;
            range.Y1 = 0d;
            range.Y2 = maximumDensity;
        }
        PositionFrequencyLabels();
    }

    private void PositionFrequencyLabels()
    {
        if (surface.GridLinesPanel is not FrameworkElement { ActualWidth: > 0 } plot) return;
        var cursors = new[] { pointCursor, lowCursor, highCursor }
            .Where(cursor => !cursor.IsHidden).OrderBy(cursor => Convert.ToDouble(cursor.X1)).ToArray();
        if (cursors.Length == 0 || cursors.Any(cursor => cursor.AnnotationLabels[0].ActualWidth == 0)) return;
        var origin = plot.TranslatePoint(new Point(), surface).X;
        var calculator = xAxis.GetCurrentCoordinateCalculator();
        var centers = cursors.Select(cursor => origin + calculator.GetCoordinate(Convert.ToDouble(cursor.X1))).ToArray();
        var widths = cursors.Select(cursor => cursor.AnnotationLabels[0].ActualWidth).ToArray();
        var lefts = centers.Select((center, index) =>
            Math.Clamp(center - widths[index] / 2, 0, Math.Max(0, surface.ActualWidth - widths[index]))).ToArray();
        if (cursors.Length == 2 && lefts[0] + widths[0] + 4 > lefts[1])
        {
            var totalWidth = widths[0] + widths[1] + 4;
            lefts[0] = Math.Clamp((centers[0] + centers[1] - totalWidth) / 2,
                0, Math.Max(0, surface.ActualWidth - totalWidth));
            lefts[1] = lefts[0] + widths[0] + 4;
        }
        for (var index = 0; index < cursors.Length; index++)
        {
            var translation = (TranslateTransform)cursors[index].AnnotationLabels[0].RenderTransform;
            translation.X = lefts[index] - (centers[index] - widths[index] / 2);
        }
    }

    private void ApplyDraggedFrequency(VerticalLineAnnotation annotation, double frequency)
    {
        if (Model is not { HasBins: true } model || !double.IsFinite(frequency)) return;
        frequency = Math.Clamp(frequency, model.MinimumFrequencyHz, model.MaximumFrequencyHz);
        if (ReferenceEquals(annotation, pointCursor)) model.SelectedFrequencyHz = frequency;
        else if (ReferenceEquals(annotation, lowCursor)) model.RangeLowHz = frequency;
        else if (ReferenceEquals(annotation, highCursor)) model.RangeHighHz = frequency;
        UpdateSelection();
    }

}
