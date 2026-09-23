using System.Windows.Threading;

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
            Exception? failure = null;
            try
            {
                frame = await Task.Run(() => WaveformDisplayFrameBuilder.Build(
                    request.Source,
                    request.DisplayWindowSeconds,
                    request.HorizontalPixels));
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

                completed(new WaveformFrameBuildResult(request, frame, failure));
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
    double PlotHeight);

internal sealed record WaveformFrameBuildResult(
    WaveformFrameBuildRequest Request,
    WaveformDisplayFrame? Frame,
    Exception? Failure);
