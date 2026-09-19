using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Acquisition.Session;

/// <summary>
/// Compares published discovery facts without relying on collection-reference
/// equality. An unchanged availability probe must not replace WPF-bound device
/// descriptors because that would clear a current selection.
/// </summary>
internal static class DeviceSessionDiscoveryComparer
{
    public static bool AreEquivalent(
        IEnumerable<AcquisitionDeviceDescriptor> current,
        IEnumerable<AcquisitionDeviceDescriptor> discovered) =>
        current.OrderBy(device => device.DeviceId, StringComparer.Ordinal)
            .SequenceEqual(
                discovered.OrderBy(device => device.DeviceId, StringComparer.Ordinal),
                DeviceComparer.Instance);

    private sealed class DeviceComparer : IEqualityComparer<AcquisitionDeviceDescriptor>
    {
        public static DeviceComparer Instance { get; } = new();

        public bool Equals(AcquisitionDeviceDescriptor? left, AcquisitionDeviceDescriptor? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is null || right is null)
            {
                return false;
            }

            return StringComparer.Ordinal.Equals(left.DeviceId, right.DeviceId) &&
                   StringComparer.Ordinal.Equals(left.DisplayName, right.DisplayName) &&
                   StringComparer.Ordinal.Equals(left.SerialNumber, right.SerialNumber) &&
                   StringComparer.Ordinal.Equals(left.Model, right.Model) &&
                   StringComparer.Ordinal.Equals(left.DeviceInstanceId, right.DeviceInstanceId) &&
                   StringComparer.Ordinal.Equals(left.DriverId, right.DriverId) &&
                   HaveSameValues(left.SupportedSamplingRatesHz, right.SupportedSamplingRatesHz) &&
                   HaveSameValues(left.ReferenceRangesVolts, right.ReferenceRangesVolts) &&
                   HaveSameValues(left.BipolarRangesVolts, right.BipolarRangesVolts) &&
                   HaveSameCapabilities(left.ChannelCapabilities, right.ChannelCapabilities);
        }

        public int GetHashCode(AcquisitionDeviceDescriptor device) =>
            StringComparer.Ordinal.GetHashCode(device.DeviceId);

        private static bool HaveSameValues<T>(IEnumerable<T>? left, IEnumerable<T>? right) where T : IComparable<T> =>
            (left ?? []).Order().SequenceEqual((right ?? []).Order());

        private static bool HaveSameCapabilities(
            IEnumerable<AcquisitionChannelCapability>? left,
            IEnumerable<AcquisitionChannelCapability>? right) =>
            (left ?? [])
                .OrderBy(capability => capability.NativeChannelIndex)
                .ThenBy(capability => capability.Kind)
                .ThenBy(capability => capability.Unit, StringComparer.Ordinal)
                .SequenceEqual(
                    (right ?? [])
                        .OrderBy(capability => capability.NativeChannelIndex)
                        .ThenBy(capability => capability.Kind)
                        .ThenBy(capability => capability.Unit, StringComparer.Ordinal));
    }
}
