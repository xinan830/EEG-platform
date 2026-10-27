using System.Windows;
using System.Windows.Media;
using SciChart.Charting;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.PointMarkers;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;
using BrainPlatform.Desktop.Modules.Review.ViewModels;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>Owns the optional real-sample marker series for review traces.</summary>
internal sealed class ReviewSampleMarkerLayer(SciChartSurface surface)
{
    private readonly List<MarkerTrace> traces = [];
    private string[] channelNames = [];
    private RecordingReviewFrame? markerFrame;
    private double markerPlotHeight = double.NaN;
    private double markerSensitivity = double.NaN;
    private double markerMillimetersPerDip = double.NaN;

    internal void EnsureSeries(IReadOnlyList<string> names, IReadOnlyList<string>? colors)
    {
        channelNames = names.ToArray();
        traces.Clear();
        for (var index = 0; index < names.Count; index++)
        {
            var data = new XyDataSeries<double, double>();
            var color = TraceColor(colors, index);
            var series = new FastLineRenderableSeries
            {
                DataSeries = data,
                Stroke = Colors.Transparent,
                StrokeThickness = 0,
                IsVisible = false,
                PointMarker = CreatePointMarker(color),
            };
            traces.Add(new MarkerTrace(data, series));
            surface.RenderableSeries.Add(series);
        }
        InvalidateProjection();
    }

    internal void UpdateColors(IReadOnlyList<string> names, IReadOnlyList<string>? colors)
    {
        if (!channelNames.SequenceEqual(names)) return;
        for (var index = 0; index < traces.Count; index++)
            traces[index].Series.PointMarker = CreatePointMarker(TraceColor(colors, index));
    }

    internal void Refresh(
        RecordingReviewFrame? frame,
        RecordingReviewViewModel? viewModel,
        DoubleRange? visibleRange,
        Rect plotArea,
        ScreenScaleContext screenScale,
        bool isEnabled)
    {
        if (frame is null || viewModel is null || !isEnabled || visibleRange is null ||
            !ReviewSampleMarkerProjection.ShouldShowMarkers(
                frame.SamplingRateHz, visibleRange.Max - visibleRange.Min, plotArea.Width))
        {
            SetVisible(false);
            return;
        }

        var sensitivity = viewModel.SensitivityMicrovoltsPerMillimeter;
        if (ReferenceEquals(markerFrame, frame) && markerPlotHeight == plotArea.Height &&
            markerSensitivity == sensitivity &&
            markerMillimetersPerDip == screenScale.MillimetersPerDipY)
        {
            SetVisible(true);
            return;
        }

        var displayScale = ScreenScaleCalculator.VerticalDisplayScale(
            frame.OutputChannelNames.Count, plotArea.Height,
            sensitivity, screenScale.MillimetersPerDipY);
        using (surface.SuspendUpdates())
        {
            for (var traceIndex = 0; traceIndex < traces.Count; traceIndex++)
            {
                var points = ReviewSampleMarkerProjection.Build(frame, traceIndex);
                var baseline = frame.OutputChannelNames.Count - traceIndex - 0.5;
                var y = points.ValuesVolts.Select(value =>
                    baseline + value * 1_000_000d * displayScale).ToArray();
                using (traces[traceIndex].Data.SuspendUpdates())
                {
                    traces[traceIndex].Data.Clear();
                    if (points.XSeconds.Count > 0)
                        traces[traceIndex].Data.Append(points.XSeconds, y);
                }
            }
        }

        markerFrame = frame;
        markerPlotHeight = plotArea.Height;
        markerSensitivity = sensitivity;
        markerMillimetersPerDip = screenScale.MillimetersPerDipY;
        SetVisible(true);
    }

    internal void Clear()
    {
        InvalidateProjection();
        foreach (var trace in traces)
        {
            trace.Data.Clear();
            trace.Series.IsVisible = false;
        }
    }

    private void SetVisible(bool isVisible)
    {
        foreach (var trace in traces)
            if (trace.Series.IsVisible != isVisible)
                trace.Series.IsVisible = isVisible;
    }

    private void InvalidateProjection()
    {
        markerFrame = null;
        markerPlotHeight = double.NaN;
        markerSensitivity = double.NaN;
        markerMillimetersPerDip = double.NaN;
    }

    private static EllipsePointMarker CreatePointMarker(Color color) => new()
    {
        Width = 4,
        Height = 4,
        Fill = color,
    };

    private static Color TraceColor(IReadOnlyList<string>? colors, int index) =>
        WaveformTraceColors.ToColor(colors?.ElementAtOrDefault(index), index);

    private sealed record MarkerTrace(
        XyDataSeries<double, double> Data,
        FastLineRenderableSeries Series);
}
