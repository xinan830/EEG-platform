using System.Runtime.CompilerServices;

namespace BrainPlatform.Desktop.Modules.Devices.Drivers.AntEego;

internal sealed class AntEegoImpedanceStream : IAcquisitionImpedanceStream
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private readonly AntEegoNativeLease lease;
    private readonly Guid detectionSessionId;
    private readonly int streamId;
    private readonly AntEegoNativeStreamOwnership ownership;
    private long updateSequence;

    public AntEegoImpedanceStream(
        AntEegoNativeLease lease,
        Guid detectionSessionId,
        int streamId,
        ImpedanceStreamMetadata metadata,
        AntEegoNativeStreamOwnership ownership)
    {
        this.lease = lease;
        this.detectionSessionId = detectionSessionId;
        this.streamId = streamId;
        Metadata = metadata;
        this.ownership = ownership;
    }

    public ImpedanceStreamMetadata Metadata { get; }

    public async IAsyncEnumerable<ImpedanceSnapshot> ReadSnapshotsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ObjectDisposedException.ThrowIf(ownership.IsClosing, this);
            var values = lease.Invoke(api =>
            {
                ObjectDisposedException.ThrowIf(ownership.IsClosing, this);
                return api.ReadAvailableData(streamId);
            });
            if (values is null)
            {
                await Task.Delay(PollInterval, cancellationToken);
                continue;
            }

            if (values.Length != Metadata.Channels.Count)
            {
                throw new AntEegoSdkException(
                    "ANT_EEGO_IMPEDANCE_DATA_SHAPE_INVALID",
                    $"The SDK returned {values.Length} impedance values for {Metadata.Channels.Count} channels; one latest value per channel is required.");
            }

            yield return ImpedanceSnapshot.Create(
                detectionSessionId,
                Interlocked.Increment(ref updateSequence),
                DateTimeOffset.UtcNow,
                Metadata,
                values.Select(value => (double?)value).ToArray());
            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    public ValueTask DisposeAsync() => ownership.DisposeAsync();
}
