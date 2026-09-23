using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Acquisition.Storage;

public interface IAcquisitionRawWriterFactory
{
    Task<IAcquisitionRawWriter> CreateAsync(
        Guid sessionId,
        AcquisitionStreamMetadata stream,
        AcquisitionProjectContext project,
        string recordingDirectory,
        CancellationToken cancellationToken);
}

public interface IAcquisitionRawWriter : IAsyncDisposable
{
    string RecordingDirectory { get; }

    Task AppendBatchAsync(AcquisitionBatch batch, CancellationToken cancellationToken);

    Task AppendGapAsync(AcquisitionGap gap, CancellationToken cancellationToken);

    Task AppendDiagnosticAsync(string code, string detail, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken);

    Task CompleteAsync(DateTimeOffset completedAtUtc, CancellationToken cancellationToken);

    Task AbortAsync(AcquisitionFault fault, CancellationToken cancellationToken);
}
