using System.Text.Json;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Configuration;

namespace BrainPlatform.Desktop.Review;

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
        if (acquisitionMontage is not null)
        {
            var acquisitionError = GetCompatibilityError(acquisitionMontage, labelMap);
            if (acquisitionError is not null)
            {
                acquisitionMontage = null;
                acquisitionStatus = AcquisitionMontageStatus.InvalidSnapshot;
            }
        }

        foreach (var profile in currentProfiles
                     .Where(profile => profile is not null)
                     .GroupBy(profile => profile.Id, StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            var error = GetCompatibilityError(profile, labelMap);
            if (error is null)
            {
                compatible.Add(new CompatibleRecordingMontage(profile));
            }
            else
            {
                incompatible.Add(new IncompatibleRecordingMontage(profile.Id, profile.Name, error));
            }
        }

        if (acquisitionMontage is not null &&
            compatible.All(item => !string.Equals(item.Profile.Id, acquisitionMontage.Id, StringComparison.Ordinal)))
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

    private static string? GetCompatibilityError(
        MontageProfile profile,
        IReadOnlyDictionary<string, AcquisitionChannel[]> labelMap)
    {
        try
        {
            MontageValidation.Validate(
                profile.ChannelConfigurationSnapshot,
                profile.DerivedChannels,
                profile.AverageReferenceLabels);
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }

        foreach (var source in profile.DerivedChannels
                     .SelectMany(channel => new[] { channel.PositiveLabel }.Concat(channel.NegativeLabels))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!labelMap.TryGetValue(source.Trim(), out var matches))
            {
                return $"记录中缺少导联源通道“{source}”。";
            }

            if (matches.Length != 1)
            {
                return $"记录中的导联源通道“{source}”存在重复，无法唯一映射。";
            }

            if (!string.Equals(matches[0].Unit, "V", StringComparison.OrdinalIgnoreCase))
            {
                return $"导联源通道“{source}”单位不是 V。";
            }
        }

        return null;
    }

    private static bool IsSignalChannel(AcquisitionChannel channel) =>
        channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar;
}
