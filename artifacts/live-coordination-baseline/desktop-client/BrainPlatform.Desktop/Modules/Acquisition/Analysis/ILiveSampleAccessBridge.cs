using BrainPlatform.Desktop.Modules.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

/// <summary>
/// Bounded process boundary for live EEG sample access. It is independent of
/// display filtering and raw-file persistence.
/// </summary>
public interface ILiveSampleAccessBridge : IAsyncDisposable
{
    Task PublishAsync(AcquisitionAnalysisBatch batch, CancellationToken cancellationToken);
}

public sealed class NullLiveSampleAccessBridge : ILiveSampleAccessBridge
{
    public Task PublishAsync(AcquisitionAnalysisBatch batch, CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
