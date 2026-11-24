
namespace BrainPlatform.Desktop.Modules.Devices.Contracts;

/// <summary>
/// Deliberately reports no device until a separately validated vendor adapter
/// is installed. It never fabricates discovery or stream data.
/// </summary>
public sealed class UnavailableAcquisitionDeviceAdapter : IAcquisitionDeviceAdapter
{
    public DeviceReadiness GetReadiness() => DeviceReadiness.NotConfigured();

    public Task<IReadOnlyList<AcquisitionDeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AcquisitionDeviceDescriptor>>([]);

    public Task<IAcquisitionStream> OpenEegStreamAsync(
        AcquisitionStreamRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<IAcquisitionStream>(
            new AcquisitionUnavailableException("No validated EEG acquisition adapter is installed."));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
