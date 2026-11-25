using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Channels.Domain;

namespace BrainPlatform.Desktop.Modules.Channels.Services;

public enum EegChannelProjectionStatus
{
    Applied,
    MissingSnapshot,
    InvalidSnapshot,
}

public sealed record EegChannelDisplayItem(
    int StreamIndex,
    int NativeChannelIndex,
    AcquisitionChannelKind Kind,
    string Label,
    int DisplayOrder);

public sealed record EegChannelProjectionResult(
    EegChannelProjectionStatus Status,
    IReadOnlyList<EegChannelDisplayItem> Channels);

/// <summary>Projects configured EEG visibility without changing source channel indexes.</summary>
public static class EnabledEegChannelProjection
{
    public static EegChannelProjectionResult Project(
        ChannelConfigurationProfile? snapshot,
        IEnumerable<AcquisitionChannel> sourceChannels)
    {
        ArgumentNullException.ThrowIfNull(sourceChannels);
        var sources = sourceChannels
            .Where(IsEeg)
            .OrderBy(channel => channel.StreamIndex)
            .ToArray();

        if (snapshot is null)
        {
            return Fallback(EegChannelProjectionStatus.MissingSnapshot, sources);
        }

        if (!IsValidSnapshot(snapshot, sources))
        {
            return InvalidSnapshot();
        }

        var byNativeIndex = sources.ToDictionary(channel => channel.NativeChannelIndex);
        var displayed = snapshot.Channels
            .Where(channel => channel.IsSelectedForDisplay && byNativeIndex.ContainsKey(channel.NativeChannelIndex))
            .OrderBy(channel => channel.DisplayOrder)
            .Select(channel =>
            {
                var source = byNativeIndex[channel.NativeChannelIndex];
                return new EegChannelDisplayItem(
                    source.StreamIndex,
                    source.NativeChannelIndex,
                    source.Kind,
                    channel.ElectrodeLabel.Trim(),
                    channel.DisplayOrder);
            })
            .ToArray();
        return new EegChannelProjectionResult(EegChannelProjectionStatus.Applied, displayed);
    }

    public static EegChannelProjectionResult InvalidSnapshot() =>
        new(EegChannelProjectionStatus.InvalidSnapshot, []);

    public static EegChannelProjectionResult PreserveSourceChannels(
        EegChannelProjectionStatus status,
        IEnumerable<AcquisitionChannel> sourceChannels)
    {
        if (status != EegChannelProjectionStatus.MissingSnapshot)
        {
            throw new ArgumentOutOfRangeException(nameof(status),
                "Only missing channel snapshots may preserve all source EEG channels.");
        }
        ArgumentNullException.ThrowIfNull(sourceChannels);
        return Fallback(status, sourceChannels.Where(IsEeg).OrderBy(channel => channel.StreamIndex));
    }

    private static bool IsValidSnapshot(
        ChannelConfigurationProfile snapshot,
        IReadOnlyList<AcquisitionChannel> sources)
    {
        if (snapshot.Channels is null || snapshot.Channels.Count == 0 || sources.Count == 0 ||
            sources.Count > snapshot.Channels.Count)
        {
            return false;
        }

        var nativeIndexes = new HashSet<int>();
        var streamIndexes = new HashSet<int>();
        var displayOrders = new HashSet<int>();
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceByNativeIndex = new Dictionary<int, AcquisitionChannel>();
        foreach (var source in sources)
        {
            if (source.StreamIndex < 0 || source.NativeChannelIndex < 0 ||
                !streamIndexes.Add(source.StreamIndex) ||
                !sourceByNativeIndex.TryAdd(source.NativeChannelIndex, source))
            {
                return false;
            }
        }

        var matchedSources = new HashSet<int>();
        foreach (var channel in snapshot.Channels)
        {
            var label = channel.ElectrodeLabel?.Trim() ?? string.Empty;
            if (channel.NativeChannelIndex < 0 || channel.DisplayOrder < 0 ||
                channel.ExpectedKind is not (AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar) ||
                !nativeIndexes.Add(channel.NativeChannelIndex) || !displayOrders.Add(channel.DisplayOrder) ||
                (label.Length > 0 && !labels.Add(label)) ||
                (channel.IsSelectedForDisplay && label.Length == 0))
            {
                return false;
            }
            if (!sourceByNativeIndex.TryGetValue(channel.NativeChannelIndex, out var source)) continue;
            if (source.Kind != channel.ExpectedKind ||
                (label.Length > 0 && !string.Equals(source.Label?.Trim(), label, StringComparison.OrdinalIgnoreCase))) return false;
            matchedSources.Add(channel.NativeChannelIndex);
        }

        return matchedSources.Count == sources.Count;
    }

    private static EegChannelProjectionResult Fallback(
        EegChannelProjectionStatus status,
        IEnumerable<AcquisitionChannel> sources) => new(
        status,
        sources.Select((channel, index) => new EegChannelDisplayItem(
            channel.StreamIndex,
            channel.NativeChannelIndex,
            channel.Kind,
            channel.Label?.Trim() ?? string.Empty,
            index)).ToArray());

    private static bool IsEeg(AcquisitionChannel channel) =>
        channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar;
}
