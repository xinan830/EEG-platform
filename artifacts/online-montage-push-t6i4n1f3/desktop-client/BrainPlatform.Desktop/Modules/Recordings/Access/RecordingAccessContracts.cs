using System.IO;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Review.Playback;

namespace BrainPlatform.Desktop.Modules.Recordings.Access;

public static class RecordingAccessErrorCodes
{
    public const string IdentityInvalid = "RECORDING_ACCESS_IDENTITY_INVALID";
    public const string RangeInvalid = "RECORDING_ACCESS_RANGE_INVALID";
    public const string NoData = "RECORDING_ACCESS_NO_DATA";
    public const string SessionClosed = "RECORDING_ACCESS_SESSION_CLOSED";
    public const string SourceUnavailable = "RECORDING_ACCESS_SOURCE_UNAVAILABLE";
}

public sealed class RecordingAccessException : IOException
{
    public RecordingAccessException(
        string code,
        string message,
        Guid? recordingId = null,
        long? requestedStartSample = null,
        long? requestedEndSample = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        RecordingId = recordingId;
        RequestedStartSample = requestedStartSample;
        RequestedEndSample = requestedEndSample;
    }

    public string Code { get; }
    public Guid? RecordingId { get; }
    public long? RequestedStartSample { get; }
    public long? RequestedEndSample { get; }
}

public sealed record RecordingSampleRange(long StartSample, long EndSampleExclusive)
{
    public long Length => checked(EndSampleExclusive - StartSample);

    public void Validate()
    {
        if (StartSample < 0 || EndSampleExclusive <= StartSample)
        {
            throw new RecordingAccessException(
                RecordingAccessErrorCodes.RangeInvalid,
                "录音读取范围必须是非负的半开样本区间，且结束样本必须大于起始样本。",
                requestedStartSample: StartSample,
                requestedEndSample: EndSampleExclusive);
        }
    }
}

public sealed record RecordingAccessMetadata(
    Guid RecordingId,
    string Revision,
    int SamplingRateHz,
    long FirstSampleCounter,
    long LastSampleCounter,
    IReadOnlyList<AcquisitionGap> Gaps,
    LocalRawRecordingManifest Manifest)
{
    public double DurationSeconds =>
        checked(LastSampleCounter - FirstSampleCounter + 1L) / (double)SamplingRateHz;

    public void Validate()
    {
        if (RecordingId == Guid.Empty || string.IsNullOrWhiteSpace(Revision))
            throw new RecordingAccessException(RecordingAccessErrorCodes.IdentityInvalid, "录音身份或版本无效。", RecordingId);
        if (SamplingRateHz <= 0 || LastSampleCounter < FirstSampleCounter)
            throw new RecordingAccessException(RecordingAccessErrorCodes.SourceUnavailable, "录音采样范围或采样率无效。", RecordingId);
    }
}

public sealed record RecordingAccessReadResult(
    RecordingSampleRange RequestedRange,
    RecordingSampleRange ActualRange,
    IReadOnlyList<AcquisitionChannel> Channels,
    RecordingReviewWindow Window);

public interface IRecordingAccessSession : IAsyncDisposable
{
    ValueTask IAsyncDisposable.DisposeAsync() => ValueTask.CompletedTask;

    LocalRawRecordingManifest Manifest { get; }
    IReadOnlyList<RecordingLifecycleBoundary> LifecycleBoundaries => [];
    double DurationSeconds { get; }
    long FirstSampleCounter => 0;
    long LastSampleCounter => checked(FirstSampleCounter +
        (long)Math.Ceiling(DurationSeconds * Manifest.SamplingRateHz) - 1L);
    RecordingAccessMetadata Metadata => new(
        Manifest.SessionId,
        $"session:{Manifest.SessionId:N}",
        Manifest.SamplingRateHz,
        FirstSampleCounter,
        LastSampleCounter,
        [],
        Manifest);

    Task<RecordingAccessReadResult> ReadSamplesAsync(
        RecordingSampleRange range,
        IReadOnlyList<int>? channelIndexes,
        CancellationToken cancellationToken);

    long GetContiguousSegmentOrigin(long sampleCounter) => FirstSampleCounter;
}
