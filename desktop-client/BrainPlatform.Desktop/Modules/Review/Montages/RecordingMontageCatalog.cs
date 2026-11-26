using System.Text.Json;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.Services;
using BrainPlatform.Desktop.Modules.Montages.Domain;

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
    IReadOnlyList<IncompatibleRecordingMontage> Incompatible,
    EegChannelProjectionStatus ChannelProjectionStatus = EegChannelProjectionStatus.Applied)
{
    public bool IsChannelProjectionUnavailable =>
        ChannelProjectionStatus == EegChannelProjectionStatus.InvalidSnapshot;

    public string AcquisitionMontageStatusText => AcquisitionMontageStatus switch
    {
        AcquisitionMontageStatus.Available => AcquisitionMontage?.DisplayName ?? "已记录导联",
        AcquisitionMontageStatus.MissingSnapshot => "采集时未记录导联快照",
        AcquisitionMontageStatus.InvalidSnapshot => "采集时导联快照不可读取",
        _ => "采集导联不可用",
    };

    public string ChannelProjectionStatusText => ChannelProjectionStatus switch
    {
        EegChannelProjectionStatus.Applied => "已应用记录通道显示配置",
        EegChannelProjectionStatus.MissingSnapshot => "未记录通道配置快照，按原始 EEG 通道显示",
        EegChannelProjectionStatus.InvalidSnapshot => "通道配置快照无效，未回退显示全通道",
        _ => "通道投影不可用",
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
        var signalChannels = manifest.Channels.Where(IsSignalChannel).ToArray();
        var rawProjection = ProjectRawSignalChannels(manifest);
        var rawSignalView = new RawSignalViewDefinition(
            rawProjection.Channels
                .Select(channel => channel.Label)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToArray());
        var labelMap = signalChannels
            .Where(channel => !string.IsNullOrWhiteSpace(channel.Label))
            .GroupBy(channel => channel.Label!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var compatible = new List<CompatibleRecordingMontage>();
        var incompatible = new List<IncompatibleRecordingMontage>();
        var acquisitionMontage = ReadAcquisitionMontage(manifest, out var acquisitionStatus, incompatible);
        var recordingSnapshot = ReadChannelConfigurationSnapshot(manifest);
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

        // Imported EDF/BDF recordings are strictly self-contained: only the
        // validated montages carried by their own sidecar snapshots belong in
        // the selector, never current system profiles or fabricated presets.
        var importedProfiles = ReadMontageCatalogueSnapshot(manifest, incompatible);
        var acquisitionMontageAdded = false;
        IEnumerable<MontageProfile> candidates;
        if (IsImportedRecording(manifest))
        {
            candidates = (acquisitionMontage is null ? Array.Empty<MontageProfile>() : [acquisitionMontage])
                .Concat(importedProfiles);
        }
        else
        {
            // Official presets are generated, not stored with user profiles. Use
            // the recorded channel snapshot so current device edits cannot replace
            // this recording's source mapping; all candidates still pass validation.
            var systemProfiles = recordingSnapshot is null || rawProjection.Status != EegChannelProjectionStatus.Applied
                ? Array.Empty<MontageProfile>()
                : SystemMontageProfileFactory.Create(recordingSnapshot);
            candidates = systemProfiles
                .Concat(importedProfiles)
                .Concat(currentProfiles);
        }

        foreach (var profile in MontageProfileCatalogue.Deduplicate(
                     candidates.Where(profile => profile is not null)))
        {
            // Keep the exact recorded snapshot instance when a candidate represents
            // the same montage without visual or formula changes, while still letting
            // updated local revisions (e.g. edited trace colors) remain selectable.
            if (ReferenceEquals(profile, acquisitionMontage) ||
                (acquisitionMontage is not null && IsEquivalentRecordedMontage(profile, acquisitionMontage)))
            {
                if (!acquisitionMontageAdded)
                {
                    compatible.Add(new CompatibleRecordingMontage(acquisitionMontage));
                    acquisitionMontageAdded = true;
                }
                continue;
            }

            var error = RecordingMontageCompatibility.GetError(profile, labelMap, recordingSnapshot);
            if (error is null)
            {
                compatible.Add(new CompatibleRecordingMontage(profile));
            }
            else
            {
                incompatible.Add(new IncompatibleRecordingMontage(profile.Id, profile.DisplayName, error));
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
            incompatible,
            rawProjection.Status);
    }

    internal static EegChannelProjectionResult ProjectRawSignalChannels(LocalRawRecordingManifest manifest)
    {
        var hasSnapshot = manifest.HardwareConfiguration.TryGetValue(
            "channel_configuration_snapshot_json", out var snapshotJson);
        if (!hasSnapshot || string.IsNullOrWhiteSpace(snapshotJson))
        {
            return EnabledEegChannelProjection.PreserveSourceChannels(
                EegChannelProjectionStatus.MissingSnapshot, manifest.Channels);
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<ChannelConfigurationProfile>(snapshotJson, JsonOptions);
            return snapshot is null
                ? EnabledEegChannelProjection.InvalidSnapshot()
                : EnabledEegChannelProjection.Project(snapshot, manifest.Channels);
        }
        catch (JsonException)
        {
            return EnabledEegChannelProjection.InvalidSnapshot();
        }
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
        out AcquisitionMontageStatus status,
        List<IncompatibleRecordingMontage> incompatible)
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
            var profile = RecordingMontageSnapshot.Resolve(
                JsonSerializer.Deserialize<MontageProfile>(snapshotJson, JsonOptions));

            status = AcquisitionMontageStatus.Available;
            return profile;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            status = AcquisitionMontageStatus.InvalidSnapshot;
            incompatible.Add(new IncompatibleRecordingMontage("recorded-acquisition", "采集导联", exception.Message));
            return null;
        }
    }

    private static IReadOnlyList<MontageProfile> ReadMontageCatalogueSnapshot(
        LocalRawRecordingManifest manifest,
        List<IncompatibleRecordingMontage> incompatible)
    {
        if (!manifest.HardwareConfiguration.TryGetValue(
                "montage_catalogue_snapshot_json", out var snapshotJson) ||
            string.IsNullOrWhiteSpace(snapshotJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new JsonException("导联目录快照必须为数组。");
            var profiles = new List<MontageProfile>();
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                try
                {
                    profiles.Add(RecordingMontageSnapshot.Resolve(entry.Deserialize<MontageProfile>(JsonOptions)));
                }
                catch (Exception exception) when (exception is JsonException or InvalidOperationException)
                {
                    incompatible.Add(new IncompatibleRecordingMontage("imported-entry", "导入导联", exception.Message));
                }
            }
            return profiles;
        }
        catch (JsonException exception)
        {
            incompatible.Add(new IncompatibleRecordingMontage("imported-catalogue", "导入导联目录", exception.Message));
            return [];
        }
    }

    private static bool IsEquivalentRecordedMontage(MontageProfile candidate, MontageProfile recorded)
    {
        if (string.Equals(candidate.Id, recorded.Id, StringComparison.Ordinal) &&
            string.Equals(candidate.Fingerprint, recorded.Fingerprint, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(candidate.Id, recorded.Id, StringComparison.Ordinal) &&
            !(MontageProfileCatalogue.CreateSystemPresetSemanticIdentity(candidate) is { } identity &&
              identity == MontageProfileCatalogue.CreateSystemPresetSemanticIdentity(recorded)))
        {
            return false;
        }

        if (candidate.DerivedChannels.Count != recorded.DerivedChannels.Count)
        {
            return false;
        }

        var candidateAverage = (candidate.AverageReferenceLabels ?? [])
            .Select(label => label.Trim())
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase);
        var recordedAverage = (recorded.AverageReferenceLabels ?? [])
            .Select(label => label.Trim())
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase);
        if (!candidateAverage.SequenceEqual(recordedAverage, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return candidate.DerivedChannels
            .OrderBy(channel => channel.DisplayOrder)
            .Zip(recorded.DerivedChannels.OrderBy(channel => channel.DisplayOrder))
            .All(pair =>
                pair.First.DisplayOrder == pair.Second.DisplayOrder &&
                string.Equals(pair.First.Name.Trim(), pair.Second.Name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pair.First.PositiveLabel.Trim(), pair.Second.PositiveLabel.Trim(), StringComparison.OrdinalIgnoreCase) &&
                pair.First.NegativeKind == pair.Second.NegativeKind &&
                string.Equals(pair.First.Color?.Trim(), pair.Second.Color?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                pair.First.NegativeLabels.Select(label => label.Trim())
                    .SequenceEqual(pair.Second.NegativeLabels.Select(label => label.Trim()), StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsImportedRecording(LocalRawRecordingManifest manifest) =>
        string.Equals(manifest.PayloadFormat, "edf-bdf", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(manifest.DeviceId, "imported", StringComparison.OrdinalIgnoreCase);

    private static bool IsSignalChannel(AcquisitionChannel channel) =>
        channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar;
}
