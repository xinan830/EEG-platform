using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Configuration;

public enum MontageProfileSource
{
    System,
    User,
}

/// <summary>The supported V1 negative input for one derived EEG waveform.</summary>
public enum MontageNegativeKind
{
    /// <summary>Device-provided signal with its fixed hardware REF and no software rereference.</summary>
    OriginalHardwareReference,
    Channel,
    Mean,
    SpecifiedPair,
}

/// <summary>
/// A display montage is a named, immutable-at-save interpretation of a channel
/// configuration. It never changes raw recorder input or analysis reference.
/// </summary>
public sealed record MontageProfile(
    string Id,
    string Name,
    string Description,
    MontageProfileSource Source,
    string DeviceSignature,
    string ChannelConfigurationFingerprint,
    ChannelConfigurationProfile ChannelConfigurationSnapshot,
    IReadOnlyList<DerivedMontageChannel> DerivedChannels,
    int Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Fingerprint = "",
    IReadOnlyList<string>? AverageReferenceLabels = null)
{
    public string SourceLabel => Source == MontageProfileSource.System ? "系统默认" : "我的配置";

    public bool CanDelete => Source == MontageProfileSource.User;

    [JsonIgnore]
    public bool CanEdit => Source == MontageProfileSource.User;

    [JsonIgnore]
    public string OpenActionLabel => CanEdit ? "编辑" : "查看";

    [JsonIgnore]
    public bool CanCopy => true;

    public int ChannelCount => DerivedChannels.Count;

    public string CreatedAtText => Source == MontageProfileSource.System
        ? "系统预置"
        : CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string UpdatedAtText => Source == MontageProfileSource.System
        ? "系统预置"
        : UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    [JsonIgnore]
    public string DescriptionPreview => Abbreviate(Description, 15);

    [JsonIgnore]
    public string ChannelConfigurationName => ChannelConfigurationSnapshot.Name;

    [JsonIgnore]
    public string ChannelSnapshotStatusLabel { get; init; } = "待校验";

    [JsonIgnore]
    public string ChannelSnapshotStatusDetail { get; init; } = "刷新后校验来源通道配置。";

    /// <summary>Short status shown and filtered by the montage list page.</summary>
    [JsonIgnore]
    public string ListStatusLabel => ChannelSnapshotStatusLabel == "已同步"
        ? "可用"
        : ChannelSnapshotStatusLabel;

    [JsonIgnore]
    public string ReferenceSummary => string.Join("、", DerivedChannels
        .Select(channel => channel.NegativeKind)
        .Distinct()
        .Select(MontageDisplay.TextFor));

    private static string Abbreviate(string? value, int maximumTextElements)
    {
        var text = value?.Trim() ?? string.Empty;
        var starts = StringInfo.ParseCombiningCharacters(text);
        return starts.Length <= maximumTextElements
            ? text
            : string.Concat(text.AsSpan(0, starts[maximumTextElements]), "…");
    }
}

public sealed record DerivedMontageChannel(
    string Name,
    string PositiveLabel,
    MontageNegativeKind NegativeKind,
    IReadOnlyList<string> NegativeLabels,
    int DisplayOrder)
{
    [JsonIgnore]
    public string FormulaText => MontageDisplay.FormatFormula(PositiveLabel, NegativeKind, NegativeLabels);
}

public static class MontageDisplay
{
    public static string CreateName(string positive, MontageNegativeKind kind, string? negative) => kind switch
    {
        MontageNegativeKind.OriginalHardwareReference => $"{positive}-REF",
        MontageNegativeKind.Channel => string.IsNullOrWhiteSpace(negative) ? positive : $"{positive}-{negative}",
        MontageNegativeKind.Mean => $"{positive}-AVG",
        MontageNegativeKind.SpecifiedPair => $"{positive}-PAIR",
        _ => positive,
    };

    public static string TextFor(MontageNegativeKind kind) => kind switch
    {
        MontageNegativeKind.OriginalHardwareReference => "原始硬件参考",
        MontageNegativeKind.Channel => "指定通道",
        MontageNegativeKind.Mean => "平均参考",
        MontageNegativeKind.SpecifiedPair => "指定双参考",
        _ => "未知",
    };

    public static string FormatFormula(string positive, MontageNegativeKind kind, IReadOnlyList<string> negatives) => kind switch
    {
        MontageNegativeKind.OriginalHardwareReference => $"{positive}-REF",
        MontageNegativeKind.Channel => $"{positive} - {negatives.SingleOrDefault() ?? "?"}",
        MontageNegativeKind.Mean => $"{positive} - Mean({string.Join(", ", negatives)})",
        MontageNegativeKind.SpecifiedPair => $"{positive} - Mean({string.Join(", ", negatives)})",
        _ => positive,
    };
}

public static class ChannelConfigurationFingerprint
{
    /// <summary>Hashes semantic mapping fields only; timestamps and profile name are not signal identity.</summary>
    public static string Create(ChannelConfigurationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var canonical = new StringBuilder()
            .Append(profile.DeviceSignature.Trim()).Append('|')
            .Append(profile.HardwareReferenceElectrodeLocation.Trim()).Append('|')
            .Append(profile.HardwareGroundElectrodeLocation.Trim());

        foreach (var channel in profile.Channels.OrderBy(channel => channel.NativeChannelIndex))
        {
            canonical.Append('|')
                .Append(channel.NativeChannelIndex).Append(':')
                .Append(channel.ExpectedKind).Append(':')
                .Append(channel.ElectrodeLabel.Trim().ToUpperInvariant()).Append(':')
                .Append(channel.IsSelectedForDisplay ? '1' : '0').Append(':')
                .Append(channel.DisplayOrder);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }
}

public static class MontageProfileFingerprint
{
    public static string Create(
        string channelConfigurationFingerprint,
        IReadOnlyList<DerivedMontageChannel> channels,
        IReadOnlyList<string>? averageReferenceLabels = null)
    {
        var canonical = new StringBuilder(channelConfigurationFingerprint.Trim());
        foreach (var channel in channels.OrderBy(channel => channel.DisplayOrder))
        {
            canonical.Append('|')
                .Append(channel.DisplayOrder).Append(':')
                .Append(channel.Name.Trim().ToUpperInvariant()).Append(':')
                .Append(channel.PositiveLabel.Trim().ToUpperInvariant()).Append(':')
                .Append(channel.NegativeKind).Append(':')
                .Append(string.Join(',', channel.NegativeLabels.Select(label => label.Trim().ToUpperInvariant())));
        }
        canonical.Append("|AVG:")
            .Append(string.Join(',', (averageReferenceLabels ?? [])
                .Select(label => label.Trim().ToUpperInvariant())
                .OrderBy(label => label, StringComparer.Ordinal)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }
}
