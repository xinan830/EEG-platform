using System.Security.Cryptography;
using System.Text;

namespace BrainPlatform.Desktop.Modules.Devices.Contracts;

/// <summary>
/// The single compatibility identity for reusable channel configuration.
/// A device instance or display name intentionally never participates.
/// </summary>
public static class AcquisitionDeviceCompatibility
{
    public const string UnreportedDriverId = "driver:not-reported";

    public static string CreateSignature(
        string? driverId,
        IReadOnlyList<AcquisitionChannelCapability>? capabilities)
    {
        var normalizedCapabilities = string.Join(
            ";",
            (capabilities ?? [])
                .OrderBy(capability => capability.NativeChannelIndex)
                .ThenBy(capability => capability.Kind)
                .ThenBy(capability => capability.Unit, StringComparer.Ordinal)
                .Select(capability =>
                    $"{capability.NativeChannelIndex}:{capability.Kind}:{capability.Unit.Trim()}"));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{NormalizeDriverId(driverId)}|{normalizedCapabilities}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string NormalizeDriverId(string? driverId) =>
        string.IsNullOrWhiteSpace(driverId) ? UnreportedDriverId : driverId.Trim();
}
