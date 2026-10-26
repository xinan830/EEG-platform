using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SciChart.Charting.Visuals;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Events.Domain;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>Creates review annotations on data changes and only repositions them during playback.</summary>
internal sealed class ReviewEventMarkerLayer
{
    private readonly List<EventVisual> eventVisuals = [];
    private readonly List<LifecycleVisual> lifecycleVisuals = [];
    private readonly SciChartSurface surface;
    private readonly FrameworkElement host;

    public ReviewEventMarkerLayer(SciChartSurface surface, FrameworkElement host)
    {
        this.surface = surface;
        this.host = host;
    }

    public Canvas Canvas { get; } = new();

    public void Clear()
    {
        Canvas.Children.Clear();
        eventVisuals.Clear();
        lifecycleVisuals.Clear();
    }

    public void Rebuild(RecordingReviewViewModel? viewModel)
    {
        Clear();
        if (viewModel is null) return;

        foreach (var item in viewModel.RecordingEvents)
        {
            if (!item.IsDisplayable) continue;

            var startSeconds = RecordingTimeMapper.ToElapsedSeconds(item.StartSample, viewModel.SamplingRateHz);
            var endSeconds = item.IsInterval
                ? RecordingTimeMapper.ToElapsedSeconds(item.EndSampleExclusive, viewModel.SamplingRateHz)
                : startSeconds;
            var color = ParseColor(item.DefinitionSnapshot.Color);
            var tooltip = RecordingEventDisplayTime.FormatMarker(
                item, viewModel.RecordingStartUtc, viewModel.SamplingRateHz);
            var shape = new Border
            {
                IsHitTestVisible = !item.IsInterval,
                Background = item.IsInterval
                    ? new SolidColorBrush(Color.FromArgb(32, color.R, color.G, color.B))
                    : new SolidColorBrush(color),
                BorderBrush = item.IsInterval ? new SolidColorBrush(Color.FromArgb(150, color.R, color.G, color.B)) : null,
                BorderThickness = item.IsInterval ? new Thickness(1, 0, 1, 0) : new Thickness(0),
            };
            var label = WaveformEventTimeLabel.Create(
                viewModel.TimeDisplay.FormatPoint(startSeconds), color, tooltip);
            Canvas.Children.Add(shape);
            Canvas.Children.Add(label);
            eventVisuals.Add(new EventVisual(shape, label, startSeconds, endSeconds, item.IsInterval));
        }

        foreach (var boundary in viewModel.LifecycleBoundaries)
        {
            var relativeSample = boundary.SampleCounter - viewModel.RecordingFirstSampleCounter;
            var seconds = relativeSample / (double)viewModel.SamplingRateHz;
            var color = Color.FromRgb(51, 65, 85);
            var line = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                IsHitTestVisible = false,
            };
            var label = WaveformEventTimeLabel.Create(
                viewModel.TimeDisplay.FormatPoint(seconds), color, BoundaryText(boundary.Kind));
            Canvas.Children.Add(line);
            Canvas.Children.Add(label);
            lifecycleVisuals.Add(new LifecycleVisual(line, label, seconds));
        }

        Update(viewModel);
    }

    public void Update(RecordingReviewViewModel? viewModel)
    {
        if (viewModel is null || Canvas.ActualWidth <= 0 || Canvas.ActualHeight <= 0)
        {
            foreach (var visual in eventVisuals) SetVisible(visual.Shape, visual.Label, false);
            foreach (var visual in lifecycleVisuals) SetVisible(visual.Line, visual.Label, false);
            return;
        }

        var start = viewModel.ViewportStartSeconds;
        var end = Math.Min(viewModel.DurationSeconds, start + viewModel.VisibleDurationSeconds);
        var duration = Math.Max(0.001, end - start);
        var plotArea = WaveformPlotLayout.GetPlotArea(surface);
        var origin = surface.TranslatePoint(new Point(plotArea.Left, plotArea.Top), host);

        foreach (var visual in eventVisuals)
        {
            if (visual.EndSeconds < start || visual.StartSeconds > end)
            {
                SetVisible(visual.Shape, visual.Label, false);
                continue;
            }

            var left = origin.X + Math.Clamp((visual.StartSeconds - start) / duration, 0, 1) * plotArea.Width;
            var right = origin.X + Math.Clamp((visual.EndSeconds - start) / duration, 0, 1) * plotArea.Width;
            visual.Shape.Width = visual.IsInterval ? Math.Max(2, right - left) : 2;
            visual.Shape.Height = plotArea.Height;
            visual.Shape.Margin = new Thickness(left, origin.Y, 0, 0);
            WaveformEventTimeLabel.Position(Canvas, visual.Label, left, origin.Y + 3);
            SetVisible(visual.Shape, visual.Label, true);
        }

        foreach (var visual in lifecycleVisuals)
        {
            if (visual.Seconds < start || visual.Seconds > end)
            {
                SetVisible(visual.Line, visual.Label, false);
                continue;
            }

            var x = origin.X + Math.Clamp((visual.Seconds - start) / duration, 0, 1) * plotArea.Width;
            visual.Line.X1 = x;
            visual.Line.X2 = x;
            visual.Line.Y1 = origin.Y;
            visual.Line.Y2 = origin.Y + plotArea.Height;
            WaveformEventTimeLabel.Position(Canvas, visual.Label, x, origin.Y + 3);
            SetVisible(visual.Line, visual.Label, true);
        }
    }

    private static void SetVisible(UIElement first, UIElement second, bool isVisible)
    {
        var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        first.Visibility = visibility;
        second.Visibility = visibility;
    }

    private static Color ParseColor(string value)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch { return Color.FromRgb(37, 99, 235); }
    }

    private static string BoundaryText(RecordingLifecycleBoundaryKind kind) => kind switch
    {
        RecordingLifecycleBoundaryKind.Started => "开始记录",
        RecordingLifecycleBoundaryKind.Paused => "暂停记录",
        RecordingLifecycleBoundaryKind.Resumed => "恢复记录",
        RecordingLifecycleBoundaryKind.Stopped => "结束记录",
        _ => kind.ToString(),
    };

    private sealed record EventVisual(Border Shape, TextBlock Label, double StartSeconds, double EndSeconds, bool IsInterval);
    private sealed record LifecycleVisual(Line Line, TextBlock Label, double Seconds);
}
