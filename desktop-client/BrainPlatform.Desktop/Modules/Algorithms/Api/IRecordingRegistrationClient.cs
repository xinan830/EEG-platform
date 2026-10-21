using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

public interface IRecordingRegistrationClient
{
    Task<RegisteredRecording> RegisterWpfRecordingAsync(
        string sourceDirectory,
        CancellationToken cancellationToken,
        IReadOnlyList<MontageProfile>? analysisMontageProfiles = null);
}
