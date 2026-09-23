using System.IO;

namespace BrainPlatform.Desktop.Modules.Review.Playback;

public sealed record LocalRawRecordingManifest(
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

public sealed record RawBatchIndexEntry(
    string ChunkPath,
    long HeaderOffset,
    long PayloadOffset,
    long FirstSampleCounter,
    int SampleCount,
    int ChannelCount,
    DateTimeOffset ReceivedAtUtc)
{
    public long LastSampleCounter => checked(FirstSampleCounter + SampleCount - 1L);

    public long PayloadByteLength => checked((long)SampleCount * ChannelCount * sizeof(double));
}

public sealed class LocalRawRecordingIndex
{
    public LocalRawRecordingIndex(
        IReadOnlyList<RawBatchIndexEntry> batches,
        long payloadBytesReadDuringOpen)
    {
        Batches = batches;
        PayloadBytesReadDuringOpen = payloadBytesReadDuringOpen;
    }

    public IReadOnlyList<RawBatchIndexEntry> Batches { get; }

    public long PayloadBytesReadDuringOpen { get; }

    public long FirstSampleCounter => Batches[0].FirstSampleCounter;

    public long LastSampleCounter => Batches[^1].LastSampleCounter;
}

public sealed record RecordingReviewSegment(
    long FirstSampleCounter,
    int SampleCount,
    int ChannelCount,
    double[] SampleMajorValues)
{
    public long LastSampleCounter => checked(FirstSampleCounter + SampleCount - 1L);
}

public sealed record RecordingReviewWindow(
    double RequestedStartSeconds,
    double ActualStartSeconds,
    double ActualEndSeconds,
    IReadOnlyList<RecordingReviewSegment> Segments);

public sealed class RecordingReadException : IOException
{
    public RecordingReadException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class LocalRawRecording : IAsyncDisposable
{
    internal LocalRawRecording(
        LocalRawRecordingManifest manifest,
        LocalRawRecordingIndex index,
        IReadOnlyList<AcquisitionGap> gaps,
        LocalRawRecordingReader reader)
    {
        Manifest = manifest;
        Index = index;
        Gaps = gaps;
        Reader = reader;
    }

    public LocalRawRecordingManifest Manifest { get; }

    public LocalRawRecordingIndex Index { get; }

    public IReadOnlyList<AcquisitionGap> Gaps { get; }

    public LocalRawRecordingReader Reader { get; }

    public ValueTask DisposeAsync() => Reader.DisposeAsync();
}
