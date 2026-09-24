using System.IO;

namespace BrainPlatform.Desktop.Modules.Acquisition.Contracts;

public enum AcquisitionState
{
    NotConfigured,
    Discovering,
    Ready,
    Starting,
    Previewing,
    Recording,
    Paused,
    Stopping,
    Stopped,
    Faulted,
}

public enum RecordingLifecycleBoundaryKind
{
    Started,
    Paused,
    Resumed,
    Stopped,
}

public sealed record RecordingLifecycleBoundary(
    RecordingLifecycleBoundaryKind Kind,
    long SampleCounter,
    DateTimeOffset OccurredAtUtc);

public enum AcquisitionChannelKind
{
    Unknown,
    Reference,
    Bipolar,
    Trigger,
    SampleCounter,
    ImpedanceReference,
    ImpedanceGround,
    Accelerometer,
    Gyroscope,
    Magnetometer,
}

public sealed record AcquisitionChannel(
    int StreamIndex,
    int NativeChannelIndex,
    string? Label,
    AcquisitionChannelKind Kind,
    string Unit);

public sealed record AcquisitionDeviceDescriptor(
    string DeviceId,
    string DisplayName,
    string? SerialNumber,
    IReadOnlyList<int> SupportedSamplingRatesHz,
    IReadOnlyList<double>? ReferenceRangesVolts = null,
    IReadOnlyList<double>? BipolarRangesVolts = null,
    IReadOnlyList<AcquisitionChannelCapability>? ChannelCapabilities = null,
    string? Model = null,
    string? DeviceInstanceId = null,
    string? DriverId = null);

public sealed record AcquisitionChannelCapability(
    int NativeChannelIndex,
    AcquisitionChannelKind Kind,
    string Unit);

public sealed record AcquisitionProjectContext(
    string Id,
    string Number,
    string Name,
    string DirectoryPath,
    string SnapshotJson)
{
    public string RecordingsDirectory => Path.Combine(DirectoryPath, "recordings");
}

public sealed record AcquisitionStreamRequest(
    string DeviceId,
    int SamplingRateHz,
    AcquisitionProjectContext Project)
{
    public string RecordingDirectory => Project.RecordingsDirectory;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DeviceId))
        {
            throw new ArgumentException("A device identifier is required.", nameof(DeviceId));
        }

        if (SamplingRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SamplingRateHz), "Sampling rate must be positive.");
        }

        if (Project is null || string.IsNullOrWhiteSpace(Project.Id) ||
            string.IsNullOrWhiteSpace(Project.Number) || string.IsNullOrWhiteSpace(Project.Name))
        {
            throw new ArgumentException("A valid research project is required for every acquisition.", nameof(Project));
        }

        if (string.IsNullOrWhiteSpace(Project.DirectoryPath) || string.IsNullOrWhiteSpace(Project.SnapshotJson))
        {
            throw new ArgumentException("The acquisition project requires a directory and immutable snapshot.", nameof(Project));
        }
    }
}

public sealed record AcquisitionStreamMetadata(
    string DeviceId,
    string DeviceName,
    int SamplingRateHz,
    IReadOnlyList<AcquisitionChannel> Channels,
    int SampleCounterChannelIndex,
    DateTimeOffset RecordingStartUtc)
{
    public IReadOnlyDictionary<string, string> HardwareConfiguration { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public void Validate()
    {
        if (SamplingRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SamplingRateHz), "Sampling rate must be positive.");
        }

        if (Channels.Count == 0)
        {
            throw new ArgumentException("A stream must contain at least one channel.", nameof(Channels));
        }

        if (Channels.Select(channel => channel.StreamIndex).Distinct().Count() != Channels.Count)
        {
            throw new ArgumentException("Stream channel indexes must be unique.", nameof(Channels));
        }

        var sampleCounterChannel = Channels.SingleOrDefault(
            channel => channel.StreamIndex == SampleCounterChannelIndex);
        if (sampleCounterChannel is null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SampleCounterChannelIndex),
                "Sample counter index must identify a stream channel.");
        }

        if (sampleCounterChannel.Kind != AcquisitionChannelKind.SampleCounter)
        {
            throw new ArgumentException(
                "The declared sample counter channel must have the SampleCounter role.",
                nameof(SampleCounterChannelIndex));
        }
    }
}

/// <summary>
/// One vendor batch decoded into sample-major doubles. Each stream channel
/// declares its own unit: EEG is V, while counter and trigger channels retain
/// their native count/code units. No UI or scientific transform may mutate this
/// payload before raw persistence.
/// </summary>
public sealed class AcquisitionBatch
{
    public AcquisitionBatch(
        long firstSampleCounter,
        int sampleCount,
        int channelCount,
        double[] sampleMajorValues,
        DateTimeOffset receivedAtUtc)
    {
        if (firstSampleCounter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstSampleCounter));
        }

        if (sampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        }

        if (channelCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCount));
        }

        if (sampleMajorValues.Length != checked(sampleCount * channelCount))
        {
            throw new ArgumentException(
                "Batch payload length must equal sample count multiplied by channel count.",
                nameof(sampleMajorValues));
        }

        FirstSampleCounter = firstSampleCounter;
        SampleCount = sampleCount;
        ChannelCount = channelCount;
        SampleMajorValues = sampleMajorValues;
        ReceivedAtUtc = receivedAtUtc;
    }

    public long FirstSampleCounter { get; }

    public long LastSampleCounter => checked(FirstSampleCounter + SampleCount - 1L);

    public int SampleCount { get; }

    public int ChannelCount { get; }

    public double[] SampleMajorValues { get; }

    public DateTimeOffset ReceivedAtUtc { get; }
}

public sealed record AcquisitionGap(
    long FirstMissingSampleCounter,
    long LastMissingSampleCounter,
    long MissingSampleCount,
    DateTimeOffset DetectedAtUtc);

public sealed record AcquisitionStateSnapshot(
    AcquisitionState State,
    string Detail,
    Guid? SessionId,
    DateTimeOffset ChangedAtUtc);

public sealed record AcquisitionFault(
    string Code,
    string Detail,
    DateTimeOffset OccurredAtUtc);

public class AcquisitionUnavailableException(string message) : InvalidOperationException(message);

public sealed class AcquisitionContinuityException(string message) : InvalidOperationException(message);

public sealed class AcquisitionStateTransitionException(string message) : InvalidOperationException(message);
