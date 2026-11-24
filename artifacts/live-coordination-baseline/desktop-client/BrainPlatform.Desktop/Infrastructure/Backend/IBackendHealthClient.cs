using BrainPlatform.Desktop.Infrastructure.Backend;

namespace BrainPlatform.Desktop.Infrastructure.Backend;

public interface IBackendHealthClient
{
    Uri Endpoint { get; }

    Task<BackendConnectionState> CheckAsync(CancellationToken cancellationToken);
}
