
namespace BrainPlatform.Desktop.Modules.Devices.Drivers;

/// <summary>Stable identity for an installed vendor driver, not a device serial number.</summary>
public sealed record AcquisitionDriverDescriptor(
    string DriverId,
    string DisplayName,
    string SetupDescription);

/// <summary>
/// Generic connection envelope. DriverSettings stay opaque to the runtime;
/// only the selected vendor driver may interpret them.
/// </summary>
public sealed record AcquisitionDriverConfiguration(
    string DriverId,
    IReadOnlyDictionary<string, string> DriverSettings,
    IReadOnlyDictionary<int, string>? ChannelLabelsByNativeIndex = null)
{
    public string RequireSetting(string key)
    {
        if (!DriverSettings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new AcquisitionUnavailableException($"设备驱动“{DriverId}”缺少必需连接参数：{key}。");
        }

        return value.Trim();
    }

    public string? GetOptionalSetting(string key) =>
        DriverSettings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
}

/// <summary>
/// Factory boundary for one installed vendor SDK. A driver must return the
/// common acquisition adapter contract; it may not leak SDK objects past it.
/// </summary>
public interface IAcquisitionDeviceDriver
{
    AcquisitionDriverDescriptor Descriptor { get; }

    IAcquisitionDeviceAdapter CreateAdapter(AcquisitionDriverConfiguration configuration);
}
