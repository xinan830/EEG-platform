using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

public interface IRecordingRegistrationClient
{
    Task<RegisteredRecording> RegisterWpfRecordingAsync(
        string sourceDirectory,
        CancellationToken cancellationToken);
}
