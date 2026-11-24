namespace BrainPlatform.Desktop.Modules.Montages.Domain;

/// <summary>Validates a whole display-only profile against the current raw sources.</summary>
public static class TemporaryWaveformMontage
{
    public static void ValidateSources(MontageProfile profile, IEnumerable<string> sourceLabels)
    {
        MontageValidation.Validate(profile.ChannelConfigurationSnapshot, profile.DerivedChannels, profile.AverageReferenceLabels);
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new InvalidOperationException("导联配置名称不能为空。");
        var labels = sourceLabels.GroupBy(label => label.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var required = profile.DerivedChannels.SelectMany(channel => channel.NegativeLabels.Prepend(channel.PositiveLabel));
        if (required.Any(label => !labels.Contains(label.Trim())))
            throw new InvalidOperationException("在线导联来源通道缺失或无法唯一识别，请重新配置。");
    }
}
