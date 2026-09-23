using BrainPlatform.Desktop.Domain;

namespace BrainPlatform.Desktop.Services;

public interface IBackendHealthClient
{
    Uri Endpoint { get; }

    Task<BackendConnectionState> CheckAsync(CancellationToken cancellationToken);
}
