using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Acquisition.Analysis;

/// <summary>
/// Future boundary for bounded Python analysis input. Implementations must not
/// write SQLite Runs or scientific artifacts from the desktop process.
/// </summary>
public interface IAcquisitionAnalysisBridge
{
    Task PublishAsync(AcquisitionAnalysisBatch batch, CancellationToken cancellationToken);
}

public sealed record AcquisitionAnalysisBatch(
    Guid SessionId,
    AcquisitionStreamMetadata Stream,
    AcquisitionBatch Batch,
    AcquisitionGap? GapBeforeBatch,
    string RawRecordingDirectory);

public sealed class NullAcquisitionAnalysisBridge : IAcquisitionAnalysisBridge
{
    public Task PublishAsync(AcquisitionAnalysisBatch batch, CancellationToken cancellationToken) => Task.CompletedTask;
}
