using BrainPlatform.Desktop.Modules.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Modules.Devices.Contracts;

public enum ImpedanceReadingStatus
{
    Valid,
    Missing,
    Invalid,
}

public enum ImpedanceCapabilityStatus
{
    Unknown,
    Supported,
    Unsupported,
}

public sealed record ImpedanceChannelIdentity(int NativeChannelIndex, AcquisitionChannelKind Kind);

public sealed record ImpedanceChannel(
    int StreamIndex,
    int NativeChannelIndex,
    AcquisitionChannelKind Kind,
    string? Label);

public sealed record ImpedanceStreamRequest(
    Guid DetectionSessionId,
    string DeviceId,
    IReadOnlyList<ImpedanceChannelIdentity> Channels)
{
    public void Validate()
    {
        if (DetectionSessionId == Guid.Empty)
            throw new ArgumentException("A detection session identifier is required.", nameof(DetectionSessionId));
        if (string.IsNullOrWhiteSpace(DeviceId))
            throw new ArgumentException("A device identifier is required.", nameof(DeviceId));
        ArgumentNullException.ThrowIfNull(Channels);
        if (Channels.Count == 0 || Channels.Any(channel => channel is null || channel.NativeChannelIndex < 0 ||
            channel.Kind != AcquisitionChannelKind.Reference) ||
            Channels.Distinct().Count() != Channels.Count)
            throw new ArgumentException("Physical impedance channel identities must be nonempty, use supported roles, and be unique.", nameof(Channels));
    }
}

public sealed record ImpedanceStreamMetadata(
    string DeviceId,
    string DeviceName,
    IReadOnlyList<ImpedanceChannel> Channels,
    IReadOnlyList<ImpedanceChannelIdentity>? UnavailableRequestedChannels = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DeviceId) || string.IsNullOrWhiteSpace(DeviceName))
            throw new ArgumentException("Impedance stream device identity is required.");
        ArgumentNullException.ThrowIfNull(Channels);
        if (Channels.Count == 0 || Channels.Select((channel, index) => channel is not null && channel.StreamIndex == index).Any(matches => !matches))
            throw new ArgumentException("Impedance channels must preserve a nonempty, contiguous SDK stream order.", nameof(Channels));
        if (Channels.Any(channel => channel is null || channel.NativeChannelIndex < 0 ||
            channel.Kind is not (AcquisitionChannelKind.Reference or AcquisitionChannelKind.ImpedanceReference or AcquisitionChannelKind.ImpedanceGround)))
            throw new ArgumentException("Each impedance channel requires a native identity and impedance role.", nameof(Channels));
        if (Channels.Select(channel => new ImpedanceChannelIdentity(channel.NativeChannelIndex, channel.Kind)).Distinct().Count() != Channels.Count)
            throw new ArgumentException("Physical impedance identities must be unique.", nameof(Channels));
        if (UnavailableRequestedChannels?.Any(channel => channel is null || channel.NativeChannelIndex < 0 ||
            channel.Kind != AcquisitionChannelKind.Reference) == true ||
            UnavailableRequestedChannels?.Distinct().Count() != UnavailableRequestedChannels?.Count)
            throw new ArgumentException("Unavailable impedance identities must be unique physical reference inputs.", nameof(UnavailableRequestedChannels));
    }
}

public sealed record ImpedanceReading(
    int StreamIndex,
    int NativeChannelIndex,
    AcquisitionChannelKind Kind,
    string? Label,
    double? ResistanceOhms,
    ImpedanceReadingStatus Status)
{
    public static ImpedanceReading FromRaw(ImpedanceChannel channel, double? resistanceOhms) =>
        resistanceOhms is null
            ? new(channel.StreamIndex, channel.NativeChannelIndex, channel.Kind, channel.Label, null, ImpedanceReadingStatus.Missing)
            : double.IsFinite(resistanceOhms.Value) && resistanceOhms.Value >= 0
                ? new(channel.StreamIndex, channel.NativeChannelIndex, channel.Kind, channel.Label, resistanceOhms, ImpedanceReadingStatus.Valid)
                : new(channel.StreamIndex, channel.NativeChannelIndex, channel.Kind, channel.Label, null, ImpedanceReadingStatus.Invalid);
}

public sealed record ImpedanceSnapshot(
    Guid DetectionSessionId,
    long UpdateSequence,
    DateTimeOffset ReceivedAtUtc,
    IReadOnlyList<ImpedanceReading> Readings)
{
    public static ImpedanceSnapshot Create(
        Guid detectionSessionId,
        long updateSequence,
        DateTimeOffset receivedAtUtc,
        ImpedanceStreamMetadata metadata,
        IReadOnlyList<double?> valuesByStreamIndex)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(valuesByStreamIndex);
        metadata.Validate();
        if (detectionSessionId == Guid.Empty || updateSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(updateSequence), "A snapshot requires a session and positive local sequence.");
        if (receivedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Receipt time must be UTC.", nameof(receivedAtUtc));
        if (valuesByStreamIndex.Count != metadata.Channels.Count)
            throw new ArgumentException("Impedance values must match the SDK channel table.", nameof(valuesByStreamIndex));

        return new ImpedanceSnapshot(
            detectionSessionId,
            updateSequence,
            receivedAtUtc,
            metadata.Channels.Select((channel, index) => ImpedanceReading.FromRaw(channel, valuesByStreamIndex[index])).ToArray());
    }
}

public interface IAcquisitionImpedanceStream : IAsyncDisposable
{
    ImpedanceStreamMetadata Metadata { get; }

    IAsyncEnumerable<ImpedanceSnapshot> ReadSnapshotsAsync(CancellationToken cancellationToken);
}

/// <summary>Optional capability; legacy EEG-only device adapters do not implement it.</summary>
public interface IAcquisitionImpedanceDeviceAdapter
{
    ImpedanceCapabilityStatus ImpedanceCapability { get; }

    Task<IAcquisitionImpedanceStream> OpenImpedanceStreamAsync(
        ImpedanceStreamRequest request,
        CancellationToken cancellationToken);
}
