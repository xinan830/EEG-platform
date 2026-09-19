using BrainPlatform.Desktop.Acquisition.AntEego;

namespace BrainPlatform.Desktop.Acquisition.Drivers;

/// <summary>Desktop composition root for drivers shipped with this client.</summary>
public static class DesktopAcquisitionDriverCatalog
{
    public static AcquisitionDriverRegistry CreateDefault() =>
        new([new AntEegoAcquisitionDriver()]);
}
