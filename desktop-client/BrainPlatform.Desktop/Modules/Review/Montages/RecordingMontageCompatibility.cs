using BrainPlatform.Desktop.Modules.Channels.Domain;

namespace BrainPlatform.Desktop.Modules.Review.Montages;

/// <summary>
/// Validates whether a saved montage can be applied to one immutable
/// recording mapping. This is a scientific mapping check, not a UI check.
/// </summary>
public static class RecordingMontageCompatibility
{
    public static string? GetError(
        MontageProfile profile,
        IReadOnlyDictionary<string, AcquisitionChannel[]> manifestLabels,
        ChannelConfigurationProfile? recordingSnapshot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(manifestLabels);

        if (recordingSnapshot is null)
        {
            return "记录缺少采集时通道配置快照，无法验证导联映射兼容性。";
        }

        if (string.IsNullOrWhiteSpace(recordingSnapshot.Id) ||
            string.IsNullOrWhiteSpace(profile.ChannelConfigurationSnapshot.Id))
        {
            return "导联或记录缺少通道配置身份，无法验证配置来源。";
        }

        if (!string.Equals(
                profile.ChannelConfigurationSnapshot.Id,
                recordingSnapshot.Id,
                StringComparison.Ordinal))
        {
            return "导联关联的通道配置不是该记录采集时使用的通道配置。";
        }

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

        if (!string.Equals(
                profile.DeviceSignature.Trim(),
                recordingSnapshot.DeviceSignature.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return "导联绑定的设备输入布局与该记录不一致。";
        }

        if (!string.Equals(
                profile.ChannelConfigurationSnapshot.HardwareReferenceElectrodeLocation.Trim(),
                recordingSnapshot.HardwareReferenceElectrodeLocation.Trim(),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                profile.ChannelConfigurationSnapshot.HardwareGroundElectrodeLocation.Trim(),
                recordingSnapshot.HardwareGroundElectrodeLocation.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return "导联绑定的硬件参考或地电极语义与该记录不一致。";
        }

        foreach (var label in RequiredLabels(profile))
        {
            if (!manifestLabels.TryGetValue(label, out var matches))
            {
                return $"记录中缺少导联源通道“{label}”。";
            }

            if (matches.Length != 1)
            {
                return $"记录中的导联源通道“{label}”存在重复，无法唯一映射。";
            }

            if (!string.Equals(matches[0].Unit, "V", StringComparison.OrdinalIgnoreCase))
            {
                return $"导联源通道“{label}”单位不是 V。";
            }

            var candidateMatches = FindSnapshotChannels(profile.ChannelConfigurationSnapshot, label);
            if (candidateMatches.Length == 0)
            {
                return $"导联源通道“{label}”不在导联绑定的通道快照中。";
            }
            if (candidateMatches.Length != 1)
            {
                return $"导联绑定的源通道“{label}”存在重复，无法唯一映射。";
            }
            var candidateChannel = candidateMatches[0];

            var recordedMatches = recordingSnapshot.Channels
                .Where(channel => channel.NativeChannelIndex == candidateChannel.NativeChannelIndex)
                .ToArray();
            if (recordedMatches.Length == 0)
            {
                return $"记录中缺少导联源通道“{label}”对应的物理输入 CH{candidateChannel.NativeChannelIndex}。";
            }
            if (recordedMatches.Length != 1)
            {
                return $"记录中的物理输入 CH{candidateChannel.NativeChannelIndex} 存在重复，无法唯一映射。";
            }
            var recordedChannel = recordedMatches[0];

            if (recordedChannel.ExpectedKind != candidateChannel.ExpectedKind ||
                !string.Equals(
                    recordedChannel.ElectrodeLabel.Trim(),
                    candidateChannel.ElectrodeLabel.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return $"导联源通道“{label}”的物理通道映射与该记录不一致。";
            }

            if (matches[0].NativeChannelIndex != recordedChannel.NativeChannelIndex ||
                matches[0].Kind != recordedChannel.ExpectedKind ||
                !string.Equals(matches[0].Label?.Trim(), recordedChannel.ElectrodeLabel.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return $"记录实际源通道“{label}”与采集时通道快照不一致，不能应用导联。";
            }
        }

        return null;
    }

    private static IEnumerable<string> RequiredLabels(MontageProfile profile) =>
        profile.DerivedChannels
            .SelectMany(channel => new[] { channel.PositiveLabel }
                .Concat(channel.NegativeLabels))
            .Concat(profile.AverageReferenceLabels ?? [])
            .Select(label => label.Trim())
            .Where(label => label.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static ChannelConfigurationEntry[] FindSnapshotChannels(
        ChannelConfigurationProfile snapshot,
        string label) => snapshot.Channels.Where(channel =>
            channel.IsSelectedForDisplay &&
            string.Equals(channel.ElectrodeLabel.Trim(), label.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
}
