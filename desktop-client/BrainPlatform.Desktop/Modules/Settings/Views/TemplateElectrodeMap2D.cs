using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Channels.Api;
using BrainPlatform.Desktop.Shared.Controls;

namespace BrainPlatform.Desktop.Modules.Settings.Views;

/// <summary>Read-only 2D scalp projection renderer for standard MNE electrode montages.</summary>
public sealed class TemplateElectrodeMap2D : FrameworkElement
{
    private const double HitRadiusPx = 14.0;
    private const double HeadRadiusScale = 0.43;

    public static readonly DependencyProperty SitesProperty = DependencyProperty.Register(
        nameof(Sites), typeof(IReadOnlyList<ElectrodeMontageSite>), typeof(TemplateElectrodeMap2D),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSitesChanged));

    public static readonly DependencyProperty SelectedChannelProperty = DependencyProperty.Register(
        nameof(SelectedChannel), typeof(string), typeof(TemplateElectrodeMap2D),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowLabelsProperty = DependencyProperty.Register(
        nameof(ShowLabels), typeof(bool), typeof(TemplateElectrodeMap2D),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush DotBrush = CreateBrush("#2563EB");
    private static readonly Brush NonFieldDotBrush = CreateBrush("#64748B");
    private static readonly Brush SelectedBrush = CreateBrush("#F97316");
    private static readonly Brush HoverBrush = CreateBrush("#0EA5E9");
    private static readonly Brush LabelBrush = CreateBrush("#1E293B");
    private static readonly Brush BadgeBgBrush = CreateBrush("#0F172A");

    private string? hoveredChannel;

    public IReadOnlyList<ElectrodeMontageSite>? Sites
    {
        get => (IReadOnlyList<ElectrodeMontageSite>?)GetValue(SitesProperty);
        set => SetValue(SitesProperty, value);
    }

    public string? SelectedChannel
    {
        get => (string?)GetValue(SelectedChannelProperty);
        set => SetValue(SelectedChannelProperty, value);
    }

    public bool ShowLabels
    {
        get => (bool)GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    public event EventHandler<ElectrodeMontageSite>? SiteClicked;

    private static void OnSitesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TemplateElectrodeMap2D)d).hoveredChannel = null;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var headBounds = ScalpMapRenderer.DrawHead(
            drawingContext,
            ActualWidth,
            ActualHeight,
            ScalpMapRenderer.VerticalOffset,
            ScalpMapRenderer.ReservedHeight,
            dpi,
            radiusScale: HeadRadiusScale);
        if (headBounds.IsEmpty || Sites is not { Count: > 0 } sites) return;

        var dotRadius = sites.Count > 160 ? 2.5 : sites.Count > 70 ? 3.2 : 4.2;
        var fontSize = sites.Count > 160 ? 8.5 : sites.Count > 70 ? 9.0 : 10.0;
        var typeface = new Typeface("Segoe UI");
        var placedRects = new List<Rect>();
        var dotRects = new List<Rect>(sites.Count);
        var orderedSites = sites
            .OrderByDescending(item => string.Equals(item.Channel, item.CanonicalChannel, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var site in sites)
        {
            var pt = ScalpMapRenderer.ToCanvas(site.MapX, site.MapY, headBounds);
            dotRects.Add(new Rect(pt.X - dotRadius - 0.8, pt.Y - dotRadius - 0.8, dotRadius * 2 + 1.6, dotRadius * 2 + 1.6));
            var isSelected = IsSameChannel(site.Channel, SelectedChannel);
            var isHovered = IsSameChannel(site.Channel, hoveredChannel);
            var fill = isSelected
                ? SelectedBrush
                : isHovered
                    ? HoverBrush
                    : site.IsScalpFieldSite ? DotBrush : NonFieldDotBrush;

            if (isSelected || isHovered)
            {
                drawingContext.DrawEllipse(
                    null,
                    new Pen(isSelected ? SelectedBrush : HoverBrush, 1.8),
                    pt,
                    dotRadius + 3.2,
                    dotRadius + 3.2);
            }

            drawingContext.DrawEllipse(fill, new Pen(Brushes.White, 1.0), pt, dotRadius, dotRadius);
        }

        if (ShowLabels)
        {
            var headCenterY = headBounds.Y + headBounds.Height / 2;
            var headRadius = headBounds.Width / 2;
            var labelBounds = new Rect(
                Math.Max(2, headBounds.Left - 18),
                Math.Max(2, headBounds.Top - 12),
                Math.Min(ActualWidth - 4, headBounds.Width + 36),
                Math.Min(ActualHeight - 4, headBounds.Height + 24));

            foreach (var site in orderedSites)
            {
                if (IsSameChannel(site.Channel, SelectedChannel) || IsSameChannel(site.Channel, hoveredChannel))
                    continue;
                var pt = ScalpMapRenderer.ToCanvas(site.MapX, site.MapY, headBounds);
                var text = new FormattedText(
                    site.Channel,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    LabelBrush,
                    dpi);

                if (TryPickLabelRect(pt, site.MapX, headCenterY, headRadius, dotRadius, text, labelBounds, placedRects, dotRects) is { } chosen)
                {
                    placedRects.Add(chosen);
                    drawingContext.DrawText(text, new Point(chosen.X + 1.5, chosen.Y));
                }
            }
        }

        DrawFocusLabel(drawingContext, headBounds, sites, hoveredChannel, false, dotRadius, dpi, typeface, ActualWidth, ActualHeight);
        DrawFocusLabel(drawingContext, headBounds, sites, SelectedChannel, true, dotRadius, dpi, typeface, ActualWidth, ActualHeight);
    }

    private static Rect? TryPickLabelRect(
        Point pt,
        double mapX,
        double headCenterY,
        double headRadius,
        double dotRadius,
        FormattedText text,
        Rect labelBounds,
        List<Rect> placedRects,
        List<Rect> dotRects)
    {
        var w = text.Width + 3;
        var h = text.Height + 1;
        var preferAbove = pt.Y > headCenterY + headRadius * 0.65;
        var below = new Rect(pt.X - w / 2, pt.Y + dotRadius + 1.5, w, h);
        var above = new Rect(pt.X - w / 2, pt.Y - dotRadius - h - 1, w, h);
        var outward = mapX < 0
            ? new Rect(pt.X - dotRadius - w - 2.5, pt.Y - h / 2, w, h)
            : new Rect(pt.X + dotRadius + 2.5, pt.Y - h / 2, w, h);
        var inward = mapX < 0
            ? new Rect(pt.X + dotRadius + 2.5, pt.Y - h / 2, w, h)
            : new Rect(pt.X - dotRadius - w - 2.5, pt.Y - h / 2, w, h);

        Rect[] candidates = preferAbove ? [above, outward, inward, below] : [below, above, outward, inward];
        foreach (var cand in candidates)
        {
            if (cand.Left < labelBounds.Left || cand.Right > labelBounds.Right ||
                cand.Top < labelBounds.Top || cand.Bottom > labelBounds.Bottom)
                continue;
            if (placedRects.Any(existing => existing.IntersectsWith(cand)))
                continue;
            if (dotRects.Any(dot => dot.IntersectsWith(cand)))
                continue;
            return cand;
        }
        return null;
    }

    private static void DrawFocusLabel(
        DrawingContext context,
        Rect headBounds,
        IReadOnlyList<ElectrodeMontageSite> sites,
        string? channel,
        bool isSelected,
        double dotRadius,
        double dpi,
        Typeface typeface,
        double canvasWidth,
        double canvasHeight)
    {
        if (string.IsNullOrWhiteSpace(channel)) return;
        var target = sites.FirstOrDefault(item => IsSameChannel(item.Channel, channel));
        if (target is null) return;

        var pt = ScalpMapRenderer.ToCanvas(target.MapX, target.MapY, headBounds);
        var text = new FormattedText(
            target.Channel,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            11.5,
            Brushes.White,
            dpi);
        var boxW = text.Width + 12;
        var boxH = text.Height + 4;
        var rawX = Math.Clamp(pt.X - boxW / 2, 4, Math.Max(4, canvasWidth - boxW - 4));
        var rawY = pt.Y - dotRadius - boxH - 5;
        if (rawY < 4) rawY = Math.Min(Math.Max(4, canvasHeight - boxH - 4), pt.Y + dotRadius + 6);
        var pill = new Rect(rawX, rawY, boxW, boxH);
        context.DrawRoundedRectangle(isSelected ? SelectedBrush : BadgeBgBrush, null, pill, 4, 4);
        context.DrawText(text, new Point(pill.X + 6, pill.Y + 2));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTestSite(e.GetPosition(this));
        var nextHover = hit?.Channel;
        if (!string.Equals(nextHover, hoveredChannel, StringComparison.Ordinal))
        {
            hoveredChannel = nextHover;
            Cursor = hit is not null ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoveredChannel is not null)
        {
            hoveredChannel = null;
            Cursor = Cursors.Arrow;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var hit = HitTestSite(e.GetPosition(this));
        if (hit is null) return;
        SelectedChannel = hit.Channel;
        SiteClicked?.Invoke(this, hit);
    }

    private ElectrodeMontageSite? HitTestSite(Point mouse)
    {
        var headBounds = ScalpMapRenderer.GetHeadBounds(
            ActualWidth,
            ActualHeight,
            ScalpMapRenderer.VerticalOffset,
            ScalpMapRenderer.ReservedHeight,
            HeadRadiusScale);
        if (headBounds.IsEmpty || Sites is not { Count: > 0 } sites) return null;

        ElectrodeMontageSite? best = null;
        var bestDistSq = HitRadiusPx * HitRadiusPx;
        foreach (var site in sites)
        {
            var pt = ScalpMapRenderer.ToCanvas(site.MapX, site.MapY, headBounds);
            var dx = mouse.X - pt.X;
            var dy = mouse.Y - pt.Y;
            var distSq = dx * dx + dy * dy;
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                best = site;
            }
        }
        return best;
    }

    private static bool IsSameChannel(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static Brush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
