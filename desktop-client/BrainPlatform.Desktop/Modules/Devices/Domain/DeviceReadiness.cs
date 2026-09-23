namespace BrainPlatform.Desktop.Modules.Devices.Domain;

public sealed record DeviceReadiness(bool IsAvailable, string StatusText, string Detail)
{
    public static DeviceReadiness NotConfigured() => new(
        false,
        "未配置",
        "尚未安装经过实机验证的设备适配器");
}
