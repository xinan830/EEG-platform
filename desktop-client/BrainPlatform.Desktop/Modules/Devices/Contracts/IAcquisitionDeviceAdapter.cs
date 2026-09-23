
namespace BrainPlatform.Desktop.Modules.Devices.Contracts;

/// <summary>
/// Hardware boundary. The ANT implementation belongs behind this interface and
/// must return the channel table reported by the opened stream.
/// </summary>
public interface IAcquisitionDeviceAdapter : IAsyncDisposable
{
    DeviceReadiness GetReadiness();

    Task<IReadOnlyList<AcquisitionDeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken);

    Task<IAcquisitionStream> OpenEegStreamAsync(
        AcquisitionStreamRequest request,
        CancellationToken cancellationToken);
}

public interface IAcquisitionStream : IAsyncDisposable
{
    AcquisitionStreamMetadata Metadata { get; }

    IAsyncEnumerable<AcquisitionBatch> ReadBatchesAsync(CancellationToken cancellationToken);
}
