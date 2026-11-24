
namespace BrainPlatform.Desktop.Modules.Channels.Validation;

/// <summary>Validates reusable channel-profile semantics without changing device state.</summary>
public static class ChannelConfigurationValidation
{
    public static void Validate(ChannelConfigurationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Channels.Count == 0)
        {
            throw new InvalidOperationException("通道配置必须包含设备返回的 EEG 输入。");
        }
        if (!IsValidMapPosition(profile.ReferenceMapPosition) || !IsValidMapPosition(profile.GroundMapPosition))
        {
            throw new InvalidOperationException("REF/GND 图示位置必须是头部圆形范围内的有限归一化坐标。");
        }

        var nativeIndexes = new HashSet<int>();
        var displayOrders = new HashSet<int>();
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var canonicalSites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var displayedCount = 0;
        foreach (var channel in profile.Channels)
        {
            if (channel.ExpectedKind is not (AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar))
            {
                throw new InvalidOperationException($"设备输入 {channel.NativeChannelIndex} 不是可配置的 EEG 输入。");
            }
            if (!nativeIndexes.Add(channel.NativeChannelIndex))
            {
                throw new InvalidOperationException($"设备输入编号 {channel.NativeChannelIndex} 重复。");
            }
            if (channel.DisplayOrder < 0 || !displayOrders.Add(channel.DisplayOrder))
            {
                throw new InvalidOperationException("通道显示顺序必须为非负且不能重复。");
            }

            var label = channel.ElectrodeLabel.Trim();
            if (!string.IsNullOrWhiteSpace(label) && !labels.Add(label))
            {
                throw new InvalidOperationException($"电极标签不能重复：{label}。");
            }
            if (channel.ElectrodeLocation is { } location)
            {
                if (!string.Equals(label, location.CanonicalSite, StringComparison.OrdinalIgnoreCase) ||
                    location.CoordinateFrame != "mri" || location.CoordinateUnit != "m" ||
                    location.SourceId != "mne.colin27_1020" ||
                    string.IsNullOrWhiteSpace(location.SourceVersion) ||
                    !double.IsFinite(location.X) || !double.IsFinite(location.Y) || !double.IsFinite(location.Z))
                {
                    throw new InvalidOperationException($"设备输入 {channel.NativeChannelIndex} 的标准电极位置与通道标签或坐标契约不一致。");
                }
                if (!canonicalSites.Add(location.CanonicalSite))
                {
                    throw new InvalidOperationException($"标准电极位置重复：{location.CanonicalSite}。");
                }
            }
            if (channel.ManualMapPosition is { } mapPosition &&
                (!double.IsFinite(mapPosition.X) || !double.IsFinite(mapPosition.Y) ||
                 Math.Abs(mapPosition.X) > 1 || Math.Abs(mapPosition.Y) > 1))
            {
                throw new InvalidOperationException($"设备输入 {channel.NativeChannelIndex} 的手动示意位置超出头部图范围。");
            }
            if (!channel.IsSelectedForDisplay)
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(label))
            {
                throw new InvalidOperationException($"设备输入 {channel.NativeChannelIndex} 已设为显示，但没有电极名称。");
            }
            displayedCount++;
        }

        if (displayedCount == 0)
        {
            throw new InvalidOperationException("请至少选择一个显示通道。");
        }
    }

    private static bool IsValidMapPosition(ElectrodeMapPosition? position) => position is null ||
        double.IsFinite(position.X) && double.IsFinite(position.Y) &&
        position.X is >= -1 and <= 1 && position.Y is >= -1 and <= 1 &&
        position.X * position.X + position.Y * position.Y <= 1;
}
