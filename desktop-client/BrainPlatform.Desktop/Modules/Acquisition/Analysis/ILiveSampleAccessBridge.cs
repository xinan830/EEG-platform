using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

/// <summary>Algorithm-neutral, bounded process boundary independent of raw persistence and display.</summary>
public interface ILiveSampleAccessBridge : IAsyncDisposable
{
    Task PublishAsync(AcquisitionAnalysisBatch batch, CancellationToken cancellationToken);
    Task SetAlgorithmEnabledAsync(string algorithmId, bool enabled, JsonElement config, CancellationToken cancellationToken);
    Task CloseSessionAsync(CancellationToken cancellationToken);
    event EventHandler<LiveAnalysisUpdate>? AnalysisUpdated;
}

public sealed class NullLiveSampleAccessBridge : ILiveSampleAccessBridge
{
    public Task PublishAsync(AcquisitionAnalysisBatch batch, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetAlgorithmEnabledAsync(string algorithmId, bool enabled, JsonElement config, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task CloseSessionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public event EventHandler<LiveAnalysisUpdate>? AnalysisUpdated { add { } remove { } }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
