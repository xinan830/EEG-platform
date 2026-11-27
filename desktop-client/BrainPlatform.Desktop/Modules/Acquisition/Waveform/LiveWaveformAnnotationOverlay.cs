using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

namespace BrainPlatform.Desktop.Modules.Acquisition.Waveform;

/// <summary>
/// Sole owner of the live waveform annotation canvas, layout signature cache,
/// event markers, and recording/pause lifecycle boundary lines.
/// </summary>
internal sealed class LiveWaveformAnnotationOverlay
{
    private string? lastMarkerLayoutSignature;

    public Canvas Visual { get; } = new();

    public bool Update(WaveformDisplayFrame? frame, LiveMonitoringViewModel? monitor, Rect plotArea)
    {
        if (frame is null || monitor is null || plotArea.Width <= 0 || plotArea.Height <= 0)
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
        Visual.Children.Clear();
        RenderEventMarkers(frame, monitor, plotArea);
        RenderLifecycleBoundaries(frame, monitor, plotArea);
        return true;
    }

    public void Clear()
    {
        Visual.Children.Clear();
        lastMarkerLayoutSignature = null;
    }

    private void RenderEventMarkers(WaveformDisplayFrame frame, LiveMonitoringViewModel monitor, Rect plotArea)
    {
        var pageStart = frame.WindowStartSampleCounter;
        var pageEndExclusive = checked(pageStart + frame.WindowSampleCount);

        foreach (var item in monitor.LiveRecordingEvents)
        {
            if (!item.IsDisplayable) continue;
            var start = monitor.ToDisplayCounterFromRecordingSample(item.StartSample);
            if (start is null) continue;
            var end = item.IsInterval
                ? monitor.ToDisplayCounterFromRecordingSample(item.EndSampleExclusive)
                : start;
            if (start.Value < pageStart || start.Value >= pageEndExclusive || end is null) continue;

            var leftFraction = (start.Value - pageStart) / (double)frame.WindowSampleCount;
            var rightFraction = item.IsInterval
                ? Math.Clamp((end.Value - pageStart) / (double)frame.WindowSampleCount, leftFraction, 1d)
                : leftFraction;
            var color = ParseColor(item.DefinitionSnapshot.Color);
            var isPendingStart = string.Equals(
                item.Id,
                AcquisitionWorkspaceViewModel.PendingIntervalEventId,
                StringComparison.Ordinal);
            var leftX = plotArea.Left + leftFraction * plotArea.Width;
            var marker = new Border
            {
                ToolTip = isPendingStart
                    ? $"{item.DefinitionSnapshot.Name}（{item.DefinitionSnapshot.EffectiveStartLabel} · 进行中）"
                    : monitor.FormatEventMarker(item),
                IsHitTestVisible = !item.IsInterval,
                Width = item.IsInterval
                    ? Math.Max(2d, (rightFraction - leftFraction) * plotArea.Width)
                    : 2d,
                Height = plotArea.Height,
                Background = item.IsInterval
                    ? new SolidColorBrush(Color.FromArgb(32, color.R, color.G, color.B))
                    : new SolidColorBrush(color),
                BorderBrush = item.IsInterval ? new SolidColorBrush(Color.FromArgb(165, color.R, color.G, color.B)) : null,
                BorderThickness = item.IsInterval ? new Thickness(1.5, 0, 1.5, 0) : new Thickness(0),
            };
            Canvas.SetLeft(marker, leftX);
            Canvas.SetTop(marker, plotArea.Top);
            Visual.Children.Add(marker);

            if (monitor.FormatEventClockTime(item) is not { } clockTime) continue;
            var startLabelText = isPendingStart
                ? $"▶ {item.DefinitionSnapshot.EffectiveStartLabel} {clockTime}"
                : item.IsInterval
                    ? $"{item.DefinitionSnapshot.EffectiveStartLabel} {clockTime}"
                    : $"{item.DefinitionSnapshot.Name} {clockTime}";
            WaveformEventTimeLabel.Add(
                Visual,
                leftX,
                plotArea.Top + 3,
                startLabelText,
                color,
                marker.ToolTip?.ToString() ?? clockTime);

            if (item.IsInterval && end.Value < pageEndExclusive)
            {
                var rightX = plotArea.Left + rightFraction * plotArea.Width;
                if (rightX - leftX >= 56)
                {
                    WaveformEventTimeLabel.Add(
                        Visual,
                        rightX,
                        plotArea.Top + 3,
                        item.DefinitionSnapshot.EffectiveEndLabel,
                        color,
                        marker.ToolTip?.ToString() ?? item.DefinitionSnapshot.EffectiveEndLabel);
                }
            }
        }
    }

    private void RenderLifecycleBoundaries(WaveformDisplayFrame frame, LiveMonitoringViewModel monitor, Rect plotArea)
    {
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
            Visual.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = plotArea.Top, Y2 = plotArea.Bottom,
                Stroke = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                IsHitTestVisible = false,
            });
            WaveformEventTimeLabel.Add(
                Visual,
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
}
