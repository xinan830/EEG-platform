
namespace BrainPlatform.Desktop.Modules.Devices.Drivers;

/// <summary>Desktop composition root for drivers shipped with this client.</summary>
public static class DesktopAcquisitionDriverCatalog
{
    public static AcquisitionDriverRegistry CreateDefault() =>
        new([new AntEegoAcquisitionDriver()]);
}
