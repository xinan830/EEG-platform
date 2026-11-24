using System.Diagnostics;
using System.Runtime.CompilerServices;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Devices.Drivers.AntEego;

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
    private readonly AntEegoNativeStreamOwnership ownership;
    private readonly long openedTimestamp = Stopwatch.GetTimestamp();
    private int closing;
    private int disposed;
    private int firstBatchLogged;
    private int noDataWarningLogged;

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
        ownership = new AntEegoNativeStreamOwnership(
            () => lease.Invoke(api => api.CloseStream(streamId)),
            () => lease.Invoke(api => api.CloseAmplifier(amplifierId)),
            lease.Dispose,
            () => this.onDisposed(this));
    }

    public AcquisitionStreamMetadata Metadata { get; }

    public async IAsyncEnumerable<AcquisitionBatch> ReadBatchesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        long? previousReadCompletedTimestamp = null;
        long? previousLastSampleCounter = null;
        var previousSampleCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var readStartedTimestamp = Stopwatch.GetTimestamp();
            var values = lease.Invoke(api =>
            {
                ThrowIfDisposed();
                return api.ReadAvailableData(streamId);
            });
            var readCompletedTimestamp = Stopwatch.GetTimestamp();
            if (values is null)
            {
                if (Stopwatch.GetElapsedTime(openedTimestamp) >= TimeSpan.FromSeconds(3) &&
                    Interlocked.Exchange(ref noDataWarningLogged, 1) == 0)
                {
                    DesktopLog.Warning($"ant_eego_eeg_no_data device_id={Metadata.DeviceId} stream_id={streamId} " +
                        $"elapsed_ms={Stopwatch.GetElapsedTime(openedTimestamp).TotalMilliseconds:F0}");
                }
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
            if (Interlocked.Exchange(ref firstBatchLogged, 1) == 0)
            {
                DesktopLog.Info($"ant_eego_eeg_first_batch device_id={Metadata.DeviceId} stream_id={streamId} " +
                    $"first_sample_counter={firstSampleCounter} sample_count={sampleCount} " +
                    $"elapsed_ms={Stopwatch.GetElapsedTime(openedTimestamp).TotalMilliseconds:F0}");
            }
            var readCallMs = Stopwatch.GetElapsedTime(readStartedTimestamp, readCompletedTimestamp).TotalMilliseconds;
            if (previousReadCompletedTimestamp is { } previousReadCompleted &&
                previousLastSampleCounter is { } previousLast && firstSampleCounter > previousLast + 1)
            {
                var interBatchMs = Stopwatch.GetElapsedTime(previousReadCompleted, readStartedTimestamp).TotalMilliseconds;
                var expectedBatchMs = previousSampleCount * 1000d / Metadata.SamplingRateHz;
                DesktopLog.Warning(
                    $"ant_eego_sample_gap first_missing={previousLast + 1} last_missing={firstSampleCounter - 1} " +
                    $"missing_count={firstSampleCounter - previousLast - 1} previous_batch_samples={previousSampleCount} " +
                    $"inter_batch_ms={interBatchMs:F1} expected_batch_ms={expectedBatchMs:F1} " +
                    $"read_call_ms={readCallMs:F1} prior_sample_counter={previousLast} next_sample_counter={firstSampleCounter}");
            }

            previousReadCompletedTimestamp = readCompletedTimestamp;
            previousLastSampleCounter = checked(firstSampleCounter + sampleCount - 1L);
            previousSampleCount = sampleCount;
            yield return new AcquisitionBatch(
                firstSampleCounter,
                sampleCount,
                channelCount,
                values,
                DateTimeOffset.UtcNow);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        Volatile.Write(ref closing, 1);
        await ownership.DisposeAsync();
        Volatile.Write(ref disposed, 1);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref closing) != 0)
        {
            throw new ObjectDisposedException(nameof(AntEegoStream));
        }
    }
}
