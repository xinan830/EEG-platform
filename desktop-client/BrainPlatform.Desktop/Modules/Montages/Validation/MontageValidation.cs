namespace BrainPlatform.Desktop.Modules.Montages.Validation;

/// <summary>Pure V1 montage validation. This does not execute any EEG computation.</summary>
public static class MontageValidation
{
    public static void Validate(
        ChannelConfigurationProfile snapshot,
        IReadOnlyList<DerivedMontageChannel> channels,
        IReadOnlyList<string>? averageReferenceLabels = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (channels.Count == 0)
        {
            throw new InvalidOperationException("导联配置至少需要一个导联。");
        }

        var labels = snapshot.Channels
            .Where(channel => channel.IsSelectedForDisplay && !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .Select(channel => channel.ElectrodeLabel.Trim())
            .ToArray();
        var duplicates = labels.GroupBy(label => label, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicates is not null)
        {
            throw new InvalidOperationException($"通道配置含重复电极标签“{duplicates.Key}”，不能创建导联。");
        }

        var available = labels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedOrders = snapshot.Channels
            .Where(channel => channel.IsSelectedForDisplay && !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .ToDictionary(channel => channel.ElectrodeLabel.Trim(), channel => channel.DisplayOrder, StringComparer.OrdinalIgnoreCase);
        var allSpecifiedChannels = channels.All(channel => channel.NegativeKind == MontageNegativeKind.Channel);
        var distinctSpecifiedReferences = allSpecifiedChannels
            ? channels.SelectMany(channel => channel.NegativeLabels)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
        var commonReference = distinctSpecifiedReferences.Length == 1 ? distinctSpecifiedReferences[0] : null;
        var isCommonChannelReference = commonReference is not null &&
            channels.All(channel => channel.NegativeLabels.Count == 1 &&
                string.Equals(channel.NegativeLabels[0], commonReference, StringComparison.OrdinalIgnoreCase));
        var isSparseChannelDerivation = allSpecifiedChannels && !isCommonChannelReference;

        if (!isSparseChannelDerivation)
        {
            var expectedPositiveLabels = isCommonChannelReference
                ? expectedOrders.Keys.Where(label => !string.Equals(label, commonReference, StringComparison.OrdinalIgnoreCase))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : expectedOrders.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actualPositiveLabels = channels.Select(channel => channel.PositiveLabel.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (channels.Count != expectedPositiveLabels.Count || !actualPositiveLabels.SetEquals(expectedPositiveLabels))
            {
                throw new InvalidOperationException(isCommonChannelReference
                    ? "公共参考导联必须覆盖除参考通道自身以外的全部显示源通道。"
                    : "该参考方式必须与通道配置的全部显示源通道一一对应。");
            }
        }

        var averageLabels = averageReferenceLabels ?? [];
        ValidateAverageReferenceGroup(averageLabels, available, channels);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orders = new HashSet<int>();
        foreach (var channel in channels)
        {
            var name = channel.Name.Trim();
            var positive = channel.PositiveLabel.Trim();
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
            {
                throw new InvalidOperationException("每条导联必须有唯一名称。");
            }
            if (!orders.Add(channel.DisplayOrder))
            {
                throw new InvalidOperationException("导联显示顺序不能重复。");
            }
            RequireAvailable(positive, available, "正极");
            if (!expectedOrders.TryGetValue(positive, out var expectedOrder) ||
                (!isSparseChannelDerivation && expectedOrder != channel.DisplayOrder))
            {
                throw new InvalidOperationException($"导联“{name}”的源通道或显示顺序与通道配置不一致。");
            }

            var negatives = channel.NegativeLabels.Select(label => label.Trim()).ToArray();
            switch (channel.NegativeKind)
            {
                case MontageNegativeKind.OriginalHardwareReference when negatives.Length != 0:
                    throw new InvalidOperationException($"导联“{name}”为原始输出时不能填写负极。");
                case MontageNegativeKind.Channel when negatives.Length != 1:
                    throw new InvalidOperationException($"导联“{name}”的指定通道必须选择一个负极。");
                case MontageNegativeKind.Mean when negatives.Length < 2:
                    throw new InvalidOperationException($"导联“{name}”的平均组参考至少需要两个通道。");
                case MontageNegativeKind.SpecifiedPair when negatives.Length != 2:
                    throw new InvalidOperationException($"导联“{name}”的指定双参考必须恰好选择两个通道。");
                case not (MontageNegativeKind.OriginalHardwareReference or MontageNegativeKind.Channel or MontageNegativeKind.Mean or MontageNegativeKind.SpecifiedPair):
                    throw new InvalidOperationException($"导联“{name}”使用了不支持的重参考方式。");
            }

            var namingNegative = channel.NegativeKind == MontageNegativeKind.Channel
                ? negatives.SingleOrDefault()
                : null;
            if (!string.Equals(name, MontageDisplay.CreateName(positive, channel.NegativeKind, namingNegative), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"导联“{name}”必须由正极和参考规则自动命名。");
            }

            if (channel.NegativeKind != MontageNegativeKind.OriginalHardwareReference)
            {
                var uniqueNegatives = negatives.ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (uniqueNegatives.Count != negatives.Length)
                {
                    throw new InvalidOperationException($"导联“{name}”的负极通道不能重复。");
                }
                foreach (var negative in negatives)
                {
                    RequireAvailable(negative, available, "负极");
                }
                if (channel.NegativeKind == MontageNegativeKind.Channel && uniqueNegatives.Contains(positive))
                {
                    throw new InvalidOperationException($"导联“{name}”的正极不能同时作为负极参考。");
                }
                if (channel.NegativeKind == MontageNegativeKind.Mean
                    && !uniqueNegatives.SetEquals(averageLabels))
                {
                    throw new InvalidOperationException($"导联“{name}”必须使用此配置保存的统一平均参考组。");
                }
            }
        }
    }

    private static void ValidateAverageReferenceGroup(
        IReadOnlyList<string> labels,
        IReadOnlySet<string> available,
        IReadOnlyList<DerivedMontageChannel> channels)
    {
        if (!channels.Any(channel => channel.NegativeKind == MontageNegativeKind.Mean))
        {
            return;
        }
        if (labels.Count < 2)
        {
            throw new InvalidOperationException("平均参考组至少需要两个有效 EEG 通道。");
        }
        var unique = labels.Select(label => label.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (unique.Count != labels.Count)
        {
            throw new InvalidOperationException("平均参考组的通道不能重复。");
        }
        foreach (var label in unique)
        {
            RequireAvailable(label, available, "平均参考组");
        }
    }

    private static void RequireAvailable(string label, IReadOnlySet<string> available, string role)
    {
        if (string.IsNullOrWhiteSpace(label) || !available.Contains(label))
        {
            throw new InvalidOperationException($"{role}通道“{label}”不在适用通道配置的显示通道内。");
        }
    }
}
