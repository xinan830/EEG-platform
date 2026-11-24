using System.Globalization;
using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Channels.Domain;

public enum ChannelConfigurationSource
{
    System,
    User,
}

/// <summary>
/// A named workstation preset. It stores user-confirmed labels, display
/// choices, and optional standard-template electrode locations. These
/// coordinates are not measured hardware or patient-specific positions.
/// </summary>
public sealed record ChannelConfigurationProfile(
    string Id,
    string Name,
    string Description,
    ChannelConfigurationSource Source,
    string DeviceSignature,
    IReadOnlyList<ChannelConfigurationEntry> Channels,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string DeviceDisplayName = "",
    string DeviceDriverId = "",
    string DeviceModel = "",
    string HardwareReferenceElectrodeLocation = "REF",
    string HardwareGroundElectrodeLocation = "GND",
    ElectrodeMapPosition? ReferenceMapPosition = null,
    ElectrodeMapPosition? GroundMapPosition = null)
{
    public string SourceLabel => Source == ChannelConfigurationSource.System ? "系统默认" : "我的配置";

    [JsonIgnore]
    public bool CanEdit => Source == ChannelConfigurationSource.User;

    [JsonIgnore]
    public string OpenActionLabel => CanEdit && !IsSignalLocked ? "编辑" : "查看";

    public bool CanDelete => Source == ChannelConfigurationSource.User && MontageReferenceCount == 0;

    [JsonIgnore]
    public bool IsActive { get; init; }

    [JsonIgnore]
    public bool HasUnappliedChanges { get; init; }

    [JsonIgnore]
    public int MontageReferenceCount { get; init; }

    [JsonIgnore]
    public bool HasMontageReferences => MontageReferenceCount > 0;

    [JsonIgnore]
    public bool IsSignalLocked => Source == ChannelConfigurationSource.System || MontageReferenceCount > 0;

    [JsonIgnore]
    public bool IsCompatibleWithCurrentDevice { get; init; }

    [JsonIgnore]
    public bool CanCreateVersion => MontageReferenceCount > 0 && IsCompatibleWithCurrentDevice;

    [JsonIgnore]
    public string ReferenceSummary => MontageReferenceCount == 0
        ? "未被导联引用"
        : $"已被 {MontageReferenceCount} 个导联引用 · 通道配置已锁定";

    public int ChannelCount => Channels.Count;

    public int EnabledChannelCount => Channels.Count(channel => channel.IsSelectedForDisplay);

    public string CreatedAtText => CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string UpdatedAtText => UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    /// <summary>Compact list-cell text; the full description remains available in the tooltip.</summary>
    [JsonIgnore]
    public string DescriptionPreview => Abbreviate(Description, 15);

    [JsonIgnore]
    public string HardwareWiringSummary =>
        $"REF：{DisplayLocation(HardwareReferenceElectrodeLocation, "REF")} · " +
        $"GND：{DisplayLocation(HardwareGroundElectrodeLocation, "GND")}";

    [JsonIgnore]
    public string StatusLabel { get; init; } = "待连接";

    [JsonIgnore]
    public string StatusDetail { get; init; } = "连接设备后验证此配置是否可用。";

    /// <summary>
    /// The single state shown in the channel-configuration list. Compatibility
    /// is evaluated first; edit locks are a separate concern and never make a
    /// disconnected or incompatible device look like a permission problem.
    /// </summary>
    [JsonIgnore]
    public string ListStatusLabel => StatusLabel == "待连接"
        ? "待连接"
        : !IsCompatibleWithCurrentDevice
            ? "设备不匹配"
            : HasMontageReferences
                ? "被导联引用"
                : IsSignalLocked
                    ? "不可修改"
                    : "可用";

    [JsonIgnore]
    public string DeviceModelLabel => !string.IsNullOrWhiteSpace(DeviceModel)
        ? DeviceModel
        : string.IsNullOrWhiteSpace(DeviceDisplayName) ? "设备未提供型号" : DeviceDisplayName;

    private static string DisplayLocation(string? location, string fallback) =>
        string.IsNullOrWhiteSpace(location) ? fallback : location.Trim();

    private static string Abbreviate(string? value, int maximumTextElements)
    {
        var text = value?.Trim() ?? string.Empty;
        var starts = StringInfo.ParseCombiningCharacters(text);
        return starts.Length <= maximumTextElements
            ? text
            : string.Concat(text.AsSpan(0, starts[maximumTextElements]), "…");
    }
}

public sealed record ChannelConfigurationEntry(
    int NativeChannelIndex,
    AcquisitionChannelKind ExpectedKind,
    string ElectrodeLabel,
    bool IsSelectedForDisplay,
    int DisplayOrder,
    ElectrodeLocationSnapshot? ElectrodeLocation = null,
    ElectrodeMapPosition? ManualMapPosition = null);

/// <summary>A user-placed 2D diagram coordinate, not a measured scalp coordinate.</summary>
public sealed record ElectrodeMapPosition(double X, double Y);

/// <summary>Default normalized diagram sites, not measured 3D coordinates.</summary>
public static class ElectrodeMapDefaults
{
    public static ElectrodeMapPosition Reference { get; } = new(0.0036, 0.4514);
    public static ElectrodeMapPosition Ground { get; } = new(0.0021, -0.7338);
}

public sealed record ElectrodeLocationSnapshot(
    string CanonicalSite,
    double X,
    double Y,
    double Z,
    string CoordinateFrame,
    string CoordinateUnit,
    string SourceId,
    string SourceVersion,
    double? MapX = null,
    double? MapY = null);
