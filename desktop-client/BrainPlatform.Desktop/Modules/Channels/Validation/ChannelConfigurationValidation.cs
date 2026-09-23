using BrainPlatform.Desktop.Acquisition.Contracts;

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

        var nativeIndexes = new HashSet<int>();
        var displayOrders = new HashSet<int>();
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
}
