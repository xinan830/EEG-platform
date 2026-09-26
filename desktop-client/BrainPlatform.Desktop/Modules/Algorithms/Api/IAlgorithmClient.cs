using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

public interface IAlgorithmClient
{
    Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken);

    Task<AnalysisRunResponse> CreateRunAsync(
        AnalysisRunRequest request,
        CancellationToken cancellationToken);

    Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken);

    Task<StructuredPreviewResponse> GetStructuredPreviewAsync(
        string runId,
        int maxCells,
        CancellationToken cancellationToken);
}
