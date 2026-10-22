using System.Text.Json;
using BrainPlatform.Desktop.Modules.Channels.Domain;

namespace BrainPlatform.Desktop.Modules.Review.Montages;

public enum AcquisitionMontageStatus
{
    Available,
    MissingSnapshot,
    InvalidSnapshot,
}

public sealed record RawSignalViewDefinition(IReadOnlyList<string> ChannelLabels);

public sealed record CompatibleRecordingMontage(MontageProfile Profile);

public sealed record IncompatibleRecordingMontage(
    string ProfileId,
    string Name,
    string Reason);

public sealed record RecordingMontageCatalogResult(
    MontageProfile? AcquisitionMontage,
    AcquisitionMontageStatus AcquisitionMontageStatus,
    RawSignalViewDefinition RawSignalView,
    IReadOnlyList<CompatibleRecordingMontage> CompatibleViewingMontages,
    IReadOnlyList<IncompatibleRecordingMontage> Incompatible)
{
    public string AcquisitionMontageStatusText => AcquisitionMontageStatus switch
    {
        AcquisitionMontageStatus.Available => AcquisitionMontage?.Name ?? "已记录导联",
        AcquisitionMontageStatus.MissingSnapshot => "采集时未记录导联快照",
        AcquisitionMontageStatus.InvalidSnapshot => "采集时导联快照不可读取",
        _ => "采集导联不可用",
    };
}

public static class RecordingMontageCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static RecordingMontageCatalogResult Build(
        LocalRawRecordingManifest manifest,
        IEnumerable<MontageProfile> currentProfiles)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(currentProfiles);
        var signalChannels = manifest.Channels
            .Where(IsSignalChannel)
            .ToArray();
        var rawSignalView = new RawSignalViewDefinition(
            signalChannels
                .Select(channel => channel.Label?.Trim())
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => label!)
                .ToArray());
        var labelMap = signalChannels
            .Where(channel => !string.IsNullOrWhiteSpace(channel.Label))
            .GroupBy(channel => channel.Label!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var compatible = new List<CompatibleRecordingMontage>();
        var incompatible = new List<IncompatibleRecordingMontage>();
        var acquisitionMontage = ReadAcquisitionMontage(manifest, out var acquisitionStatus);
        var recordingSnapshot = ReadChannelConfigurationSnapshot(manifest);
        var acquisitionMontageAdded = false;
        if (acquisitionMontage is not null)
        {
            var acquisitionError = RecordingMontageCompatibility.GetError(
                acquisitionMontage, labelMap, recordingSnapshot);
            if (acquisitionError is not null)
            {
                acquisitionMontage = null;
                acquisitionStatus = AcquisitionMontageStatus.InvalidSnapshot;
            }
        }

        // Official presets are generated, not stored with user profiles. Use
        // the recorded channel snapshot so current device edits cannot replace
        // this recording's source mapping; all candidates still pass validation.
        var systemProfiles = recordingSnapshot is null
            ? Array.Empty<MontageProfile>()
            : SystemMontageProfileFactory.Create(recordingSnapshot);
        foreach (var profile in systemProfiles.Concat(currentProfiles)
                     .Where(profile => profile is not null)
                     .GroupBy(profile => profile.Id, StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            // The current configuration can have the same identity as the
            // recorded montage while differing in UI-only fields. The review
            // selector must contain the exact recorded snapshot instance,
            // otherwise its default SelectedItem has no matching list item.
            if (acquisitionMontage is not null &&
                string.Equals(profile.Id, acquisitionMontage.Id, StringComparison.Ordinal) &&
                string.Equals(profile.Fingerprint, acquisitionMontage.Fingerprint, StringComparison.Ordinal))
            {
                compatible.Add(new CompatibleRecordingMontage(acquisitionMontage));
                acquisitionMontageAdded = true;
                continue;
            }

            var error = RecordingMontageCompatibility.GetError(profile, labelMap, recordingSnapshot);
            if (error is null)
            {
                compatible.Add(new CompatibleRecordingMontage(profile));
            }
            else
            {
                incompatible.Add(new IncompatibleRecordingMontage(profile.Id, profile.Name, error));
            }
        }

        if (acquisitionMontage is not null && !acquisitionMontageAdded)
        {
            compatible.Insert(0, new CompatibleRecordingMontage(acquisitionMontage));
        }

        return new RecordingMontageCatalogResult(
            acquisitionMontage,
            acquisitionStatus,
            rawSignalView,
            compatible,
            incompatible);
    }

    private static ChannelConfigurationProfile? ReadChannelConfigurationSnapshot(
        LocalRawRecordingManifest manifest)
    {
        if (!manifest.HardwareConfiguration.TryGetValue(
                "channel_configuration_snapshot_json",
                out var snapshotJson) ||
            string.IsNullOrWhiteSpace(snapshotJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ChannelConfigurationProfile>(snapshotJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static MontageProfile? ReadAcquisitionMontage(
        LocalRawRecordingManifest manifest,
        out AcquisitionMontageStatus status)
    {
        status = AcquisitionMontageStatus.MissingSnapshot;
        if (!manifest.HardwareConfiguration.TryGetValue(
                "montage_configuration_snapshot_json",
                out var snapshotJson) ||
            string.IsNullOrWhiteSpace(snapshotJson))
        {
            return null;
        }

        try
        {
            var profile = JsonSerializer.Deserialize<MontageProfile>(snapshotJson, JsonOptions);
            if (profile is null || string.IsNullOrWhiteSpace(profile.Id) || profile.DerivedChannels.Count == 0)
            {
                status = AcquisitionMontageStatus.InvalidSnapshot;
                return null;
            }

            status = AcquisitionMontageStatus.Available;
            return profile;
        }
        catch (JsonException)
        {
            status = AcquisitionMontageStatus.InvalidSnapshot;
            return null;
        }
    }

    private static bool IsSignalChannel(AcquisitionChannel channel) =>
        channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar;
}
