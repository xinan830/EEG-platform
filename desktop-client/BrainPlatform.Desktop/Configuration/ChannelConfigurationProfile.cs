using BrainPlatform.Desktop.Acquisition.Contracts;
using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Configuration;

public enum ChannelConfigurationSource
{
    System,
    User,
}

/// <summary>
/// A named workstation preset. It stores user-confirmed labels, display
/// choices, and operator-recorded hardware electrode locations. The amplifier
/// remains authoritative for physical inputs and never supplies those locations.
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
    string HardwareGroundElectrodeLocation = "GND")
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

    [JsonIgnore]
    public string HardwareWiringSummary =>
        $"REF：{DisplayLocation(HardwareReferenceElectrodeLocation, "REF")} · " +
        $"GND：{DisplayLocation(HardwareGroundElectrodeLocation, "GND")}";

    [JsonIgnore]
    public string StatusLabel { get; init; } = "待连接";

    [JsonIgnore]
    public string StatusDetail { get; init; } = "连接设备后验证此配置是否可用。";

    [JsonIgnore]
    public string DeviceModelLabel => !string.IsNullOrWhiteSpace(DeviceModel)
        ? DeviceModel
        : string.IsNullOrWhiteSpace(DeviceDisplayName) ? "设备未提供型号" : DeviceDisplayName;

    private static string DisplayLocation(string? location, string fallback) =>
        string.IsNullOrWhiteSpace(location) ? fallback : location.Trim();
}

public sealed record ChannelConfigurationEntry(
    int NativeChannelIndex,
    AcquisitionChannelKind ExpectedKind,
    string ElectrodeLabel,
    bool IsSelectedForDisplay,
    int DisplayOrder);
