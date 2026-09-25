using System.Windows.Threading;
using SciChart.Charting.Model.DataSeries;

namespace BrainPlatform.Desktop.Modules.Acquisition.Waveform;

/// <summary>
/// Builds only the latest requested display frame away from the WPF dispatcher.
/// Older pending requests are replaced so rendering can never accumulate debt.
/// </summary>
internal sealed class LatestWaveformFrameWorker : IDisposable
{
    private readonly object gate = new();
    private readonly Dispatcher dispatcher;
    private readonly Action<WaveformFrameBuildResult> completed;
    private WaveformFrameBuildRequest? pending;
    private long latestSequence;
    private bool isRunning;
    private bool disposed;

    public LatestWaveformFrameWorker(
        Dispatcher dispatcher,
        Action<WaveformFrameBuildResult> completed)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.completed = completed ?? throw new ArgumentNullException(nameof(completed));
    }

    public void Request(WaveformFrameBuildRequest request)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            latestSequence = request.Sequence;
            pending = request;
            if (isRunning)
            {
                return;
            }

            isRunning = true;
        }

        _ = ProcessAsync();
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            pending = null;
        }
    }

    public void CancelPending()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            latestSequence++;
            pending = null;
        }
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            WaveformFrameBuildRequest request;
            lock (gate)
            {
                if (disposed || pending is null)
                {
                    isRunning = false;
                    return;
                }

                request = pending;
                pending = null;
            }

            WaveformDisplayFrame? frame = null;
            IReadOnlyList<PreparedWaveformTrace>? preparedSeries = null;
            Exception? failure = null;
            try
            {
                frame = await Task.Run(() => WaveformDisplayFrameBuilder.Build(
                    request.Source,
                    request.DisplayWindowSeconds,
                    request.HorizontalPixels));
                if (frame is not null && request.PrepareSeriesForPageSwap)
                {
                    preparedSeries = await Task.Run(() => WaveformRenderSeriesBuilder.Build(frame, request));
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            await dispatcher.InvokeAsync(() =>
            {
                lock (gate)
                {
                    if (disposed || request.Sequence != latestSequence)
                    {
                        return;
                    }
                }

                completed(new WaveformFrameBuildResult(request, frame, preparedSeries, failure));
            }, DispatcherPriority.Render);
        }
    }
}

internal sealed record WaveformFrameBuildRequest(
    long Sequence,
    LiveWaveformSource Source,
    double DisplayWindowSeconds,
    int HorizontalPixels,
    double SensitivityMicrovoltsPerMillimeter,
    double PlotHeight,
    double MillimetersPerDipY,
    bool PrepareSeriesForPageSwap);

internal sealed record WaveformFrameBuildResult(
    WaveformFrameBuildRequest Request,
    WaveformDisplayFrame? Frame,
    IReadOnlyList<PreparedWaveformTrace>? PreparedSeries,
    Exception? Failure);

internal sealed record PreparedWaveformTrace(
    XyDataSeries<double, double> Data,
    double[] XValues,
    double[] YValues);

internal static class WaveformRenderSeriesBuilder
{
    public static IReadOnlyList<PreparedWaveformTrace> Build(
        WaveformDisplayFrame frame,
        WaveformFrameBuildRequest request)
    {
        var displayScale = ScreenScaleCalculator.VerticalDisplayScale(
            frame.Traces.Count,
            request.PlotHeight,
            request.SensitivityMicrovoltsPerMillimeter,
            request.MillimetersPerDipY);
        var prepared = new List<PreparedWaveformTrace>(frame.Traces.Count);
        for (var traceIndex = 0; traceIndex < frame.Traces.Count; traceIndex++)
        {
            var trace = frame.Traces[traceIndex];
            var baseline = frame.Traces.Count - traceIndex - .5d;
            var xValues = new List<double>(trace.Points.Count * 2);
            var yValues = new List<double>(trace.Points.Count * 2);
            var hasPreviousPoint = false;
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

            var data = new XyDataSeries<double, double>();
            if (xValues.Count > 0)
            {
                data.Append(xValues, yValues);
            }

            prepared.Add(new PreparedWaveformTrace(data, xValues.ToArray(), yValues.ToArray()));
        }

        return prepared;
    }
}
