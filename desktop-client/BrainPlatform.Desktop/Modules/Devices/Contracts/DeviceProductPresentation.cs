namespace BrainPlatform.Desktop.Modules.Devices.Contracts;

/// <summary>Application product branding; native driver identities and vendor evidence are unchanged.</summary>
public static class DeviceProductPresentation
{
    public const string Brand = "POP";

    public static string ForDriver(string? driverId, string missing) =>
        string.Equals(driverId?.Trim(), "ant-eego", StringComparison.OrdinalIgnoreCase)
            ? Brand : string.IsNullOrWhiteSpace(driverId) ? missing : driverId.Trim();

    public static string FormatDeviceName(string? serialNumber) =>
        string.IsNullOrWhiteSpace(serialNumber) ? $"{Brand} amplifier" : $"{Brand} {serialNumber}";
}
