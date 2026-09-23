using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Acquisition.Drivers;

/// <summary>Explicit installed-driver catalog; no runtime SDK probing occurs here.</summary>
public sealed class AcquisitionDriverRegistry
{
    private readonly IReadOnlyDictionary<string, IAcquisitionDeviceDriver> drivers;

    public AcquisitionDriverRegistry(IEnumerable<IAcquisitionDeviceDriver> installedDrivers)
    {
        ArgumentNullException.ThrowIfNull(installedDrivers);
        var registered = new Dictionary<string, IAcquisitionDeviceDriver>(StringComparer.Ordinal);
        foreach (var driver in installedDrivers)
        {
            ArgumentNullException.ThrowIfNull(driver);
            var id = driver.Descriptor.DriverId;
            if (string.IsNullOrWhiteSpace(id) || !registered.TryAdd(id, driver))
            {
                throw new ArgumentException("采集设备驱动 ID 必须非空且唯一。", nameof(installedDrivers));
            }
        }

        drivers = registered;
    }

    public IReadOnlyList<AcquisitionDriverDescriptor> Drivers => drivers.Values
        .Select(driver => driver.Descriptor)
        .OrderBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
        .ToArray();

    public IAcquisitionDeviceAdapter CreateAdapter(AcquisitionDriverConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return GetRequired(configuration.DriverId).CreateAdapter(configuration);
    }

    public IAcquisitionDeviceDriver GetRequired(string driverId)
    {
        if (string.IsNullOrWhiteSpace(driverId) || !drivers.TryGetValue(driverId, out var driver))
        {
            throw new AcquisitionUnavailableException($"未安装采集设备驱动：{driverId}。");
        }

        return driver;
    }
}
