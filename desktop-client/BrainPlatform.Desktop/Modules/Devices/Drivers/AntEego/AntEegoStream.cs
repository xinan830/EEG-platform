using System.Runtime.CompilerServices;
using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Acquisition.AntEego;

/// <summary>
/// Decodes one opened vendor EEG stream. It deliberately returns raw
/// sample-major values; recording and scientific processing own any later
/// transformation.
/// </summary>
internal sealed class AntEegoStream : IAcquisitionStream
{
    private readonly AntEegoNativeLease lease;
    private readonly int amplifierId;
    private readonly int streamId;
    private readonly TimeSpan emptyReadDelay;
    private readonly Action<AntEegoStream> onDisposed;
    private int disposed;

    public AntEegoStream(
        AntEegoNativeLease lease,
        int amplifierId,
        int streamId,
        AcquisitionStreamMetadata metadata,
        TimeSpan emptyReadDelay,
        Action<AntEegoStream> onDisposed)
    {
        this.lease = lease ?? throw new ArgumentNullException(nameof(lease));
        this.amplifierId = amplifierId;
        this.streamId = streamId;
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.emptyReadDelay = emptyReadDelay;
        this.onDisposed = onDisposed ?? throw new ArgumentNullException(nameof(onDisposed));
    }

    public AcquisitionStreamMetadata Metadata { get; }

    public async IAsyncEnumerable<AcquisitionBatch> ReadBatchesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        while (!cancellationToken.IsCancellationRequested)
        {
            var values = lease.Invoke(api => api.ReadAvailableData(streamId));
            if (values is null)
            {
                await Task.Delay(emptyReadDelay, cancellationToken);
                continue;
            }

            var channelCount = Metadata.Channels.Count;
            if (values.Length == 0 || values.Length % channelCount != 0)
            {
                throw new AntEegoSdkException(
                    "ANT_EEGO_DATA_SHAPE_INVALID",
                    "SDK data does not contain a whole number of opened-stream samples.");
            }

            var sampleCount = values.Length / channelCount;
            var firstSampleCounter = AntEegoSampleCounterReader.ReadAndValidateFirstCounter(
                values,
                sampleCount,
                channelCount,
                Metadata.SampleCounterChannelIndex);
            yield return new AcquisitionBatch(
                firstSampleCounter,
                sampleCount,
                channelCount,
                values,
                DateTimeOffset.UtcNow);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        Exception? closeFailure = null;
        try
        {
            lease.Invoke(api => api.CloseStream(streamId));
        }
        catch (Exception exception)
        {
            closeFailure = exception;
        }

        try
        {
            lease.Invoke(api => api.CloseAmplifier(amplifierId));
        }
        catch (Exception exception)
        {
            closeFailure ??= exception;
        }
        finally
        {
            try
            {
                lease.Dispose();
            }
            finally
            {
                onDisposed(this);
            }
        }

        return closeFailure is null
            ? ValueTask.CompletedTask
            : ValueTask.FromException(closeFailure);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(AntEegoStream));
        }
    }
}
