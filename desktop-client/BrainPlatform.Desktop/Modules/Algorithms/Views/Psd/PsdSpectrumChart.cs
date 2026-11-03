using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.Psd;

public sealed class PsdSpectrumChart : FrameworkElement
{
    private INotifyCollectionChanged? observedCollection;
    private Point? hoverScreenPoint;
    private double? hoverFreq;
    private double? hoverVal;
    private bool isHovered;

    private const double PlotLeft = 0;
    private const double PlotTop = 0;
    private const double HoverHitTolerance = 8.0;

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IEnumerable), typeof(PsdSpectrumChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(PsdSpectrumChart),
        new FrameworkPropertyMetadata("µV²/Hz", FrameworkPropertyMetadataOptions.AffectsRender));

    public PsdSpectrumChart()
    {
        ClipToBounds = true;
        Cursor = Cursors.Cross;
    }

    public IEnumerable? Points
    {
        get => (IEnumerable?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    protected override void OnVisualParentChanged(DependencyObject? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        AttachCollection(Points);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == PointsProperty)
        {
            DetachCollection();
            AttachCollection(e.NewValue as IEnumerable);
            InvalidateVisual();
        }
        else if (e.Property == UnitProperty)
        {
            InvalidateVisual();
        }
    }

    private void AttachCollection(IEnumerable? value)
    {
        if (value is INotifyCollectionChanged collection)
        {
            observedCollection = collection;
            observedCollection.CollectionChanged += OnCollectionChanged;
        }
    }

    private void DetachCollection()
    {
        if (observedCollection is not null)
            observedCollection.CollectionChanged -= OnCollectionChanged;
        observedCollection = null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateHover(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHover(e.GetPosition(this));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ClearHover();
    }

    private void ClearHover()
    {
        if (!isHovered && !hoverScreenPoint.HasValue && !hoverFreq.HasValue && !hoverVal.HasValue) return;
        isHovered = false;
        hoverScreenPoint = null;
        hoverFreq = null;
        hoverVal = null;
        InvalidateVisual();
    }

    private void UpdateHover(Point pos)
    {
        if (ActualWidth <= 1 || ActualHeight <= 1)
        {
            ClearHover();
            return;
        }

        if (pos.X < 0 || pos.X > ActualWidth || pos.Y < 0 || pos.Y > ActualHeight)
        {
            ClearHover();
            return;
        }

        var (points, minX, maxX, minY, maxY) = GetCurrentData();
        if (points.Length < 2)
        {
            ClearHover();
            return;
        }

        var nearest = FindNearestCurveSample(points, pos, minX, maxX - minX, minY, maxY - minY);
        if (nearest.DistanceSquared > HoverHitTolerance * HoverHitTolerance)
        {
            ClearHover();
            return;
        }

        hoverFreq = nearest.Frequency;
        hoverVal = nearest.Value;
        hoverScreenPoint = nearest.ScreenPoint;
        isHovered = true;
        InvalidateVisual();
    }

    private (Point ScreenPoint, double Frequency, double Value, double DistanceSquared) FindNearestCurveSample(
        (double Frequency, double Value)[] points, Point pointer, double minX, double xRange, double minY, double yRange)
    {
        var nearestScreenPoint = default(Point);
        var nearestFrequency = 0.0;
        var nearestValue = 0.0;
        var nearestDistanceSquared = double.PositiveInfinity;

        for (var index = 0; index < points.Length - 1; index++)
        {
            var firstScreen = ToPoint(points[index], minX, xRange, minY, yRange);
            var secondScreen = ToPoint(points[index + 1], minX, xRange, minY, yRange);
            var deltaX = secondScreen.X - firstScreen.X;
            var deltaY = secondScreen.Y - firstScreen.Y;
            var segmentLengthSquared = deltaX * deltaX + deltaY * deltaY;
            var progress = segmentLengthSquared <= double.Epsilon ? 0.0 : Math.Clamp(
                ((pointer.X - firstScreen.X) * deltaX + (pointer.Y - firstScreen.Y) * deltaY) /
                segmentLengthSquared, 0.0, 1.0);
            var closestX = firstScreen.X + progress * deltaX;
            var closestY = firstScreen.Y + progress * deltaY;
            var distanceX = pointer.X - closestX;
            var distanceY = pointer.Y - closestY;
            var distanceSquared = distanceX * distanceX + distanceY * distanceY;
            if (distanceSquared >= nearestDistanceSquared) continue;

            var sample = progress < 0.5 ? points[index] : points[index + 1];
            nearestScreenPoint = ToPoint(sample, minX, xRange, minY, yRange);
            nearestFrequency = sample.Frequency;
            nearestValue = sample.Value;
            nearestDistanceSquared = distanceSquared;
        }

        return (nearestScreenPoint, nearestFrequency, nearestValue, nearestDistanceSquared);
    }

    private ( (double Frequency, double Value)[] Points, double MinX, double MaxX, double MinY, double MaxY ) GetCurrentData()
    {
        var raw = Points?.Cast<object>()
            .Select(item =>
            {
                if (item is null) return (Frequency: (double?)null, Value: (double?)null);
                var type = item.GetType();
                var frequencyObject = type.GetProperty("Frequency")?.GetValue(item);
                var valueObject = type.GetProperty("Value")?.GetValue(item);
                var frequency = frequencyObject is double frequencyValue ? frequencyValue : (double?)null;
                var value = valueObject is double valueValue ? valueValue : (double?)null;
                return (Frequency: frequency, Value: value);
            })
            // The PSD view is explicitly a 0–50 Hz view. Ignore malformed
            // samples outside that declared display contract instead of
            // stretching the axis and showing impossible hover frequencies.
            .Where(item => item.Frequency is double f && item.Value is double v &&
                           double.IsFinite(f) && double.IsFinite(v) && f >= 0 && f <= 50)
            .Select(item => (Frequency: item.Frequency!.Value, Value: item.Value!.Value))
            .OrderBy(item => item.Frequency)
            .ToArray();

        // Do not draw synthetic data when the backend has not produced a result.
        // A blank chart is scientifically safer than a plausible-looking curve.
        var points = raw is { Length: >= 2 } ? raw : Array.Empty<(double Frequency, double Value)>();

        var minX = 0.0;
        const double maxDisplayFrequency = 50.0;
        var maxX = maxDisplayFrequency;
        var minY = 0.0;
        var maxY = points.Length == 0 ? 55.0 : Math.Max(55.0, Math.Ceiling(points.Max(item => item.Value) / 10.0) * 10.0);

        return (points, minX, maxX, minY, maxY);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 1 || ActualHeight <= 1) return;

        var (points, minX, maxX, minY, maxY) = GetCurrentData();
        if (points.Length < 2) return;

        var xRange = Math.Max(maxX - minX, double.Epsilon);
        var yRange = Math.Max(maxY - minY, double.Epsilon);

        DrawAxes(drawingContext, minX, maxX, minY, maxY);

        // 1. Draw PSD curve
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var first = ToPoint(points[0], minX, xRange, minY, yRange);
            context.BeginFigure(first, false, false);
            foreach (var point in points.Skip(1))
                context.LineTo(ToPoint(point, minX, xRange, minY, yRange), true, false);
        }
        geometry.Freeze();

        var curvePen = new Pen(new SolidColorBrush(Color.FromRgb(22, 119, 255)), 2.2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        curvePen.Freeze();
        drawingContext.DrawGeometry(null, curvePen, geometry);

        // 2. Draw Interactive Hover elements (Guideline, Marker dot, Tooltip popover)
        if (isHovered && hoverScreenPoint.HasValue && hoverFreq.HasValue && hoverVal.HasValue)
        {
            var hPoint = hoverScreenPoint.Value;

            // Vertical guideline
            var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(170, 148, 163, 184)), 1.2);
            guidePen.Freeze();
            drawingContext.DrawLine(guidePen, new Point(hPoint.X, 0), new Point(hPoint.X, ActualHeight));

            // Marker dot on curve
            var blueBrush = new SolidColorBrush(Color.FromRgb(22, 119, 255));
            var whitePen = new Pen(Brushes.White, 2.5);
            blueBrush.Freeze();
            whitePen.Freeze();
            drawingContext.DrawEllipse(blueBrush, whitePen, hPoint, 5.5, 5.5);

            // Dark floating tooltip popover
            RenderTooltip(drawingContext, hPoint, hoverFreq.Value, hoverVal.Value);
        }
    }

    private void DrawAxes(DrawingContext dc, double minX, double maxX, double minY, double maxY)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(226, 232, 240)), 1) { DashStyle = DashStyles.Dot };
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(148, 163, 184)), 1);
        for (var i = 0; i <= 5; i++)
        {
            var x = ActualWidth * i / 5.0;
            dc.DrawLine(gridPen, new Point(x, 0), new Point(x, ActualHeight));
            DrawAxisText(dc, $"{minX + (maxX - minX) * i / 5:0.#}", new Point(x - 10, ActualHeight - 20), 11);
        }
        const int yTicks = 5;
        for (var i = 0; i <= yTicks; i++)
        {
            var y = ActualHeight - ActualHeight * i / (double)yTicks;
            dc.DrawLine(gridPen, new Point(0, y), new Point(ActualWidth, y));
            DrawAxisText(dc, FormatAxisValue(minY + (maxY - minY) * i / yTicks), new Point(4, y), 11, ActualHeight);
        }
        dc.DrawLine(axisPen, new Point(0, 0), new Point(0, ActualHeight));
        dc.DrawLine(axisPen, new Point(0, ActualHeight - 1), new Point(ActualWidth, ActualHeight - 1));
        var unit = FormatUnit(Unit);
        var label = new FormattedText($"功率谱密度 ({unit})", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Microsoft YaHei"), 11, new SolidColorBrush(Color.FromRgb(71, 85, 105)), 1.0);
        dc.PushTransform(new RotateTransform(-90, 14, ActualHeight / 2));
        dc.DrawText(label, new Point(14, ActualHeight / 2 - label.Width / 2));
        dc.Pop();
    }

    private static void DrawAxisText(DrawingContext dc, string text, Point point, double size, double? verticalBounds = null)
    {
        var formattedText = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Microsoft YaHei"), size, new SolidColorBrush(Color.FromRgb(71, 85, 105)), 1.0);
        var y = verticalBounds is double height
            ? Math.Clamp(point.Y - formattedText.Height / 2, 0, Math.Max(0, height - formattedText.Height))
            : point.Y;
        dc.DrawText(formattedText, new Point(point.X, y));
    }

    private static string FormatAxisValue(double value) => value switch
    {
        >= 1000 => value.ToString("0.0e+0", CultureInfo.InvariantCulture),
        >= 100 => value.ToString("0", CultureInfo.InvariantCulture),
        >= 1 => value.ToString("0.#", CultureInfo.InvariantCulture),
        _ => value.ToString("0.##", CultureInfo.InvariantCulture)
    };

    internal static string FormatTooltipNumber(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    internal static string FormatUnit(string? unit) =>
        string.IsNullOrWhiteSpace(unit) || unit is "uV^2/Hz" or "μV^2/Hz" ? "µV²/Hz" : unit;

    private void RenderTooltip(DrawingContext dc, Point hPoint, double freq, double val)
    {
        double pixelsPerDip = 1.0;
        try
        {
            pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        }
        catch
        {
            // fallback
        }

        var typeface = new Typeface(new FontFamily("Segoe UI, Microsoft YaHei"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var unitStr = FormatUnit(Unit);

        var textFreq = new FormattedText(
            $"频率： {FormatTooltipNumber(freq)} Hz",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            12,
            Brushes.White,
            pixelsPerDip);

        var textVal = new FormattedText(
            $"功率谱密度： {FormatTooltipNumber(val)} {unitStr}",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            12,
            Brushes.White,
            pixelsPerDip);

        const double padH = 12.0;
        const double padV = 8.0;
        var tooltipWidth = Math.Max(textFreq.Width, textVal.Width) + padH * 2;
        var tooltipHeight = textFreq.Height + textVal.Height + padV * 2 + 4.0;

        // Position tooltip near cursor, flipping if too close to borders
        var ttX = hPoint.X + 12;
        var ttY = hPoint.Y - tooltipHeight - 12;

        if (ttX + tooltipWidth > ActualWidth - 6)
            ttX = hPoint.X - tooltipWidth - 12;
        if (ttX < 6)
            ttX = 6;
        if (ttY < 6)
            ttY = hPoint.Y + 14;
        if (ttY + tooltipHeight > ActualHeight - 6)
            ttY = ActualHeight - tooltipHeight - 6;

        var bgBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)); // Slate 800
        bgBrush.Freeze();

        dc.DrawRoundedRectangle(bgBrush, null, new Rect(ttX, ttY, tooltipWidth, tooltipHeight), 8, 8);
        dc.DrawText(textFreq, new Point(ttX + padH, ttY + padV));
        dc.DrawText(textVal, new Point(ttX + padH, ttY + padV + textFreq.Height + 4));
    }

    private Point ToPoint((double Frequency, double Value) point, double minX, double xRange, double minY, double yRange) =>
        new(
            Math.Clamp(PlotLeft + (point.Frequency - minX) / xRange * Math.Max(ActualWidth - PlotLeft, 1.0), 0.0, ActualWidth),
            Math.Clamp(PlotTop + ActualHeight - (point.Value - minY) / yRange * Math.Max(ActualHeight - PlotTop, 1.0), 0.0, ActualHeight));

}
