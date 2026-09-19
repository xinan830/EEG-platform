using System.Text.Json;
using System.IO;
using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Acquisition.Storage;

/// <summary>
/// Writes immutable local raw chunks before a batch enters the display buffer
/// or any future analysis bridge. The on-disk values are sample-major float64.
/// The manifest channel table records the unit of every stream position.
/// </summary>
public sealed class LocalAcquisitionRawWriterFactory : IAcquisitionRawWriterFactory
{
    private readonly long maxChunkBytes;

    public LocalAcquisitionRawWriterFactory(long maxChunkBytes = 64L * 1024L * 1024L)
    {
        this.maxChunkBytes = maxChunkBytes > 0
            ? maxChunkBytes
            : throw new ArgumentOutOfRangeException(nameof(maxChunkBytes));
    }

    public async Task<IAcquisitionRawWriter> CreateAsync(
        Guid sessionId,
        AcquisitionStreamMetadata stream,
        AcquisitionProjectContext project,
        string recordingDirectory,
        CancellationToken cancellationToken)
    {
        stream.Validate();
        ArgumentNullException.ThrowIfNull(project);
        var rootDirectory = Path.GetFullPath(recordingDirectory);
        Directory.CreateDirectory(rootDirectory);
        var directory = Path.Combine(
            rootDirectory,
            $"{stream.RecordingStartUtc:yyyyMMdd-HHmmss}_{sessionId:N}");
        Directory.CreateDirectory(directory);

        var manifestPath = Path.Combine(directory, "manifest.json");
        var manifest = new LocalRawManifest(
            sessionId,
            "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
            "V",
            stream.DeviceId,
            stream.DeviceName,
            stream.SamplingRateHz,
            stream.SampleCounterChannelIndex,
            stream.RecordingStartUtc,
            stream.Channels,
            project,
            stream.HardwareConfiguration);

        await using (var manifestStream = new FileStream(
                         manifestPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.Read,
                         4096,
                         FileOptions.WriteThrough | FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(manifestStream, manifest, cancellationToken: cancellationToken);
            await manifestStream.FlushAsync(cancellationToken);
        }

        return new LocalAcquisitionRawWriter(directory, maxChunkBytes);
    }

    private sealed record LocalRawManifest(
        Guid SessionId,
        string PayloadFormat,
        string EegSignalUnit,
        string DeviceId,
        string DeviceName,
        int SamplingRateHz,
        int SampleCounterChannelIndex,
        DateTimeOffset RecordingStartUtc,
        IReadOnlyList<AcquisitionChannel> Channels,
        AcquisitionProjectContext Project,
        IReadOnlyDictionary<string, string> HardwareConfiguration);
}

public sealed class LocalAcquisitionRawWriter : IAcquisitionRawWriter
{
    private const long FlushThresholdBytes = 1024L * 1024L;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private readonly long maxChunkBytes;
    private readonly System.Diagnostics.Stopwatch flushClock = System.Diagnostics.Stopwatch.StartNew();
    private FileStream sampleStream;
    private BinaryWriter sampleWriter;
    private readonly StreamWriter auditWriter;
    private int chunkIndex = 1;
    private long unflushedBytes;
    private bool isClosed;

    public LocalAcquisitionRawWriter(string recordingDirectory, long maxChunkBytes)
    {
        RecordingDirectory = recordingDirectory;
        this.maxChunkBytes = maxChunkBytes > 0
            ? maxChunkBytes
            : throw new ArgumentOutOfRangeException(nameof(maxChunkBytes));
        (sampleStream, sampleWriter) = OpenChunk(recordingDirectory, chunkIndex);
        auditWriter = new StreamWriter(
            new FileStream(
                Path.Combine(recordingDirectory, "audit.jsonl"),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.WriteThrough | FileOptions.Asynchronous));
    }

    public string RecordingDirectory { get; }

    public async Task AppendBatchAsync(AcquisitionBatch batch, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        var recordBytes = checked(24L + (long)batch.SampleMajorValues.Length * sizeof(double));
        if (recordBytes > maxChunkBytes)
        {
            throw new InvalidOperationException(
                "A single acquisition batch exceeds the raw chunk limit. The device adapter must emit bounded batches.");
        }

        if (sampleStream.Length > 0 && checked(sampleStream.Length + recordBytes) > maxChunkBytes)
        {
            await FlushSamplesAsync(cancellationToken);
            sampleWriter.Dispose();
            sampleStream.Dispose();
            chunkIndex++;
            (sampleStream, sampleWriter) = OpenChunk(RecordingDirectory, chunkIndex);
        }

        sampleWriter.Write(batch.FirstSampleCounter);
        sampleWriter.Write(batch.SampleCount);
        sampleWriter.Write(batch.ChannelCount);
        sampleWriter.Write(batch.ReceivedAtUtc.UtcDateTime.Ticks);
        foreach (var value in batch.SampleMajorValues)
        {
            sampleWriter.Write(value);
        }

        unflushedBytes = checked(unflushedBytes + recordBytes);
        if (unflushedBytes >= FlushThresholdBytes || flushClock.Elapsed >= FlushInterval)
        {
            await FlushSamplesAsync(cancellationToken);
        }
    }

    public Task AppendGapAsync(AcquisitionGap gap, CancellationToken cancellationToken) =>
        AppendAuditAsync(new
        {
            type = "sample_gap",
            gap.FirstMissingSampleCounter,
            gap.LastMissingSampleCounter,
            gap.MissingSampleCount,
            detected_at_utc = gap.DetectedAtUtc,
        }, cancellationToken);

    public Task AppendDiagnosticAsync(
        string code,
        string detail,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken) =>
        AppendAuditAsync(new
        {
            type = "diagnostic",
            code,
            detail,
            occurred_at_utc = occurredAtUtc,
        }, cancellationToken);

    public async Task CompleteAsync(DateTimeOffset completedAtUtc, CancellationToken cancellationToken)
    {
        if (isClosed)
        {
            return;
        }

        await AppendAuditAsync(new { type = "completed", completed_at_utc = completedAtUtc }, cancellationToken);
        isClosed = true;
        await DisposeCoreAsync();
    }

    public async Task AbortAsync(AcquisitionFault fault, CancellationToken cancellationToken)
    {
        if (isClosed)
        {
            return;
        }

        await AppendAuditAsync(new
        {
            type = "aborted",
            fault.Code,
            fault.Detail,
            occurred_at_utc = fault.OccurredAtUtc,
        }, cancellationToken);
        isClosed = true;
        await DisposeCoreAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (isClosed)
        {
            return;
        }

        isClosed = true;
        await DisposeCoreAsync();
    }

    private async Task AppendAuditAsync<T>(T entry, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        await auditWriter.WriteLineAsync(JsonSerializer.Serialize(entry).AsMemory(), cancellationToken);
        await auditWriter.FlushAsync(cancellationToken);
    }

    private async Task DisposeCoreAsync()
    {
        await FlushSamplesAsync(CancellationToken.None);
        sampleWriter.Dispose();
        await sampleStream.DisposeAsync();
        await auditWriter.DisposeAsync();
    }

    private static (FileStream Stream, BinaryWriter Writer) OpenChunk(string recordingDirectory, int index)
    {
        var stream = new FileStream(
            Path.Combine(recordingDirectory, $"samples-{index:D6}.bin"),
            FileMode.CreateNew,
            FileAccess.Write,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        return (stream, new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true));
    }

    private async Task FlushSamplesAsync(CancellationToken cancellationToken)
    {
        sampleWriter.Flush();
        await sampleStream.FlushAsync(cancellationToken);
        unflushedBytes = 0;
        flushClock.Restart();
    }

    private void ThrowIfClosed()
    {
        if (isClosed)
        {
            throw new InvalidOperationException("The raw writer is closed.");
        }
    }
}
