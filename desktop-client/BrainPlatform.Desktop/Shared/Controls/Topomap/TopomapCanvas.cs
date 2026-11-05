using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Shared.Controls;

namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

/// <summary>Renders one backend-produced EEG band map with the shared scalp geometry.</summary>
public sealed class TopomapCanvas : FrameworkElement
{
    private static readonly Brush[] ColorScaleBrushes = CreateColorScaleBrushes();
    private TopomapVisualSnapshot? _snapshot;
    private TopomapVisualSnapshot? _diagnosticSnapshot;
    private CancellationTokenSource? _preparationCancellation;
    private int _generation;

    public static readonly DependencyProperty FieldProperty = DependencyProperty.Register(
        nameof(Field), typeof(TopomapField), typeof(TopomapCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnFieldChanged));

    public static readonly DependencyProperty ColorScaleStateProperty = DependencyProperty.Register(
        nameof(ColorScaleState), typeof(TopomapColorScaleState), typeof(TopomapCanvas),
        new FrameworkPropertyMetadata(null, OnScaleConfigurationChanged));

    public TopomapCanvas()
    {
        Loaded += (_, _) => SchedulePreparation();
        Unloaded += (_, _) =>
        {
            CancelPreparation();
            _snapshot = null;
            _diagnosticSnapshot = null;
        };
        SizeChanged += (_, _) => SchedulePreparation(clearSnapshot: true);
    }

    public TopomapField? Field
    {
        get => (TopomapField?)GetValue(FieldProperty);
        set => SetValue(FieldProperty, value);
    }

    public TopomapColorScaleState? ColorScaleState
    {
        get => (TopomapColorScaleState?)GetValue(ColorScaleStateProperty);
        set => SetValue(ColorScaleStateProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var snapshot = _snapshot is { } prepared &&
                       prepared.CanvasWidth == ActualWidth && prepared.CanvasHeight == ActualHeight
            ? prepared
            : null;
        var bounds = snapshot?.HeadBounds ?? ScalpMapRenderer.GetHeadBounds(ActualWidth, ActualHeight,
            ScalpMapRenderer.VerticalOffset, ScalpMapRenderer.ReservedHeight, ScalpMapRenderer.RadiusScale);
        if (bounds.IsEmpty) return;

        var field = snapshot?.Field ?? Field;
        var diagnostics = snapshot is not null && DesktopLog.PerformanceDiagnosticsEnabled &&
                          !ReferenceEquals(_diagnosticSnapshot, snapshot);
        var renderTimer = diagnostics ? Stopwatch.StartNew() : null;
        if (snapshot?.GridBitmap is { } bitmap && snapshot.HeadClip is { } clip)
        {
            drawingContext.PushClip(clip);
            drawingContext.DrawImage(bitmap, bounds);
            if (snapshot.Contours is { } contours && snapshot.ContourPen is { } pen)
                drawingContext.DrawGeometry(null, pen, contours);
            drawingContext.Pop();
        }

        ScalpMapRenderer.DrawHead(drawingContext, ActualWidth, ActualHeight, ScalpMapRenderer.VerticalOffset,
            ScalpMapRenderer.ReservedHeight, VisualTreeHelper.GetDpi(this).PixelsPerDip,
            fill: false, showFrontLabel: false, radiusScale: ScalpMapRenderer.RadiusScale);
        DrawElectrodes(drawingContext, field, bounds);
        DrawTitleAndScale(drawingContext, field, bounds, snapshot);
        if (diagnostics && renderTimer is not null && snapshot is not null)
        {
            _diagnosticSnapshot = snapshot;
            var message = $"review_topomap_perf stage=render field={snapshot.Field.FieldId}" +
                          $" draw_ms={renderTimer.Elapsed.TotalMilliseconds:0.0}" +
                          $" raster_ms={snapshot.RasterMilliseconds:0.0}" +
                          $" contours_ms={snapshot.ContourMilliseconds:0.0}" +
                          $" prepare_ms={snapshot.PreparationMilliseconds:0.0}";
            ThreadPool.QueueUserWorkItem(_ => DesktopLog.Info(message));
        }
    }

    private static void OnFieldChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var canvas = (TopomapCanvas)dependencyObject;
        if (args.NewValue is null)
        {
            canvas.CancelPreparation();
            canvas._snapshot = null;
            canvas._diagnosticSnapshot = null;
            canvas.InvalidateVisual();
            return;
        }
        canvas.SchedulePreparation();
    }

    private static void OnScaleConfigurationChanged(DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) => ((TopomapCanvas)dependencyObject).SchedulePreparation();

    private void SchedulePreparation(bool clearSnapshot = false)
    {
        CancelPreparation();
        if (clearSnapshot)
        {
            _snapshot = null;
            _diagnosticSnapshot = null;
        }

        if (!IsLoaded || Field is not { } field || ActualWidth < 80 || ActualHeight < 80)
        {
            InvalidateVisual();
            return;
        }

        if (_snapshot is { } current && ReferenceEquals(current.Field, field) &&
            current.CanvasWidth == ActualWidth && current.CanvasHeight == ActualHeight)
            return;

        var cancellation = new CancellationTokenSource();
        var generation = _generation;
        var width = ActualWidth;
        var height = ActualHeight;
        _preparationCancellation = cancellation;
        _ = PrepareVisualAsync(field, width, height, generation, cancellation);
        InvalidateVisual();
    }

    private async Task PrepareVisualAsync(TopomapField field, double width, double height,
        int generation, CancellationTokenSource cancellation)
    {
        var published = false;
        try
        {
            var scaleBounds = ColorScaleState?.GetBounds(field);
            var snapshot = await Task.Run(() => TopomapVisualPreparer.Prepare(
                field, width, height, cancellation.Token, scaleBounds), cancellation.Token).ConfigureAwait(false);
            if (DesktopLog.PerformanceDiagnosticsEnabled && field.Grid is { } grid)
                DesktopLog.Info($"review_topomap_perf stage=prepare field={field.FieldId}" +
                                $" grid={grid.Width}x{grid.Height}" +
                                $" raster_ms={snapshot.RasterMilliseconds:0.0}" +
                                $" contours_ms={snapshot.ContourMilliseconds:0.0}" +
                                $" prepare_ms={snapshot.PreparationMilliseconds:0.0}");

            await Dispatcher.InvokeAsync(() =>
            {
                if (cancellation.IsCancellationRequested || generation != _generation || !IsLoaded ||
                    !ReferenceEquals(Field, field) || width != ActualWidth || height != ActualHeight)
                    return;
                _preparationCancellation = null;
                _snapshot = snapshot;
                if (snapshot.HasGridValues)
                    ColorScaleState?.Remember(field, snapshot.Minimum, snapshot.Maximum);
                published = true;
                InvalidateVisual();
            }, DispatcherPriority.Render).Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            DesktopLog.Warning($"review_topomap_prepare_failed field={field.FieldId}" +
                               $" exception_type={exception.GetType().Name}");
        }
        finally
        {
            if (!published && !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
            {
                try
                {
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        if (ReferenceEquals(_preparationCancellation, cancellation))
                            _preparationCancellation = null;
                    }));
                }
                catch (InvalidOperationException) { }
            }
            cancellation.Dispose();
        }
    }

    private void CancelPreparation()
    {
        _generation++;
        var cancellation = _preparationCancellation;
        _preparationCancellation = null;
        if (cancellation is null) return;
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private static void DrawElectrodes(DrawingContext context, TopomapField? field, Rect bounds)
    {
        if (field is null) return;
        foreach (var point in field.Points)
        {
            if (point.Status != "included" || point.X is not { } x || point.Y is not { } y) continue;
            var position = ScalpMapRenderer.ToCanvas(x, y, bounds);
            context.DrawEllipse(Brushes.Black, null, position, 1.8, 1.8);
        }
    }

    private void DrawTitleAndScale(DrawingContext context, TopomapField? field, Rect bounds,
        TopomapVisualSnapshot? snapshot)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var title = new FormattedText(field is null ? "" : FieldName(field),
            CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 11, new SolidColorBrush(Color.FromRgb(15, 23, 42)), dpi);
        context.DrawText(title,
            new Point(Math.Max(0, (bounds.X - title.Width) / 2),
                bounds.Y + (bounds.Height - title.Height) / 2));
        if (snapshot is not { HasGridValues: true }) return;

        var bar = new Rect(bounds.Right + 20, bounds.Y + 4, 18, Math.Max(20, bounds.Height - 8));
        for (var index = 0; index < ColorScaleBrushes.Length; index++)
            context.DrawRectangle(ColorScaleBrushes[index], null,
                new Rect(bar.X, bar.Y + bar.Height * index / ColorScaleBrushes.Length,
                    bar.Width, bar.Height / ColorScaleBrushes.Length + 1));
        var unitLabel = new FormattedText(DisplayUnit(field?.Unit), CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 13, Brushes.SlateGray, dpi);
        context.DrawText(unitLabel, new Point(
            Math.Clamp(bar.X + (bar.Width - unitLabel.Width) / 2, 0, Math.Max(0, ActualWidth - unitLabel.Width)),
            Math.Max(0, bar.Y - 25)));
        var maximumLabel = new FormattedText(FormatScaleValue(snapshot.Maximum, field?.Unit), CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 14, Brushes.SlateGray, dpi);
        var minimumLabel = new FormattedText(FormatScaleValue(snapshot.Minimum, field?.Unit), CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 14, Brushes.SlateGray, dpi);
        var labelsFitRight = bar.Right + 6 + Math.Max(maximumLabel.Width, minimumLabel.Width) <= ActualWidth;
        context.DrawText(maximumLabel, new Point(
            labelsFitRight ? bar.Right + 6 : Math.Max(0, bar.X - maximumLabel.Width - 6), bar.Y - 4));
        context.DrawText(minimumLabel, new Point(
            labelsFitRight ? bar.Right + 6 : Math.Max(0, bar.X - minimumLabel.Width - 6), bar.Bottom - 15));
    }

    internal static string DisplayUnit(string? unit) => unit switch
    {
        "uV^2" => "µV²",
        "V^2/Hz" => "µV²/Hz",
        _ => unit ?? "",
    };

    internal static string FormatScaleValue(double value, string? unit) =>
        (unit == "V^2/Hz" ? value * 1_000_000_000_000d : value)
        .ToString("G4", CultureInfo.CurrentUICulture);

    private static Brush[] CreateColorScaleBrushes()
    {
        var brushes = new Brush[20];
        for (var index = 0; index < brushes.Length; index++)
        {
            var brush = new SolidColorBrush(TopomapVisualPreparer.ColorValue(1 - index / 19d));
            brush.Freeze();
            brushes[index] = brush;
        }
        return brushes;
    }

    private static string FieldName(TopomapField field)
    {
        if (field.FieldId.StartsWith("psd.band-mean-density.", StringComparison.Ordinal)) return field.Label;
        var id = field.FieldId.Split('.').Last();
        return id switch
        {
            "delta" => "Delta", "theta" => "Theta", "alpha" => "Alpha",
            "beta" => "Beta", "gamma" => "Gamma", _ => field.Label,
        };
    }
}
