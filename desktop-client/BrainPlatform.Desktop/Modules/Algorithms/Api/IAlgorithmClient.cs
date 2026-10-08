using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

public interface IAlgorithmClient
{
    Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken);

    Task<AnalysisRunResponse> CreateRunAsync(
        AnalysisRunRequest request,
        CancellationToken cancellationToken);

    Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken);

    Task<AnalysisRunResponse> CancelRunAsync(string runId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前算法客户端不支持取消分析。");

    Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken);

    Task<StructuredPreviewResponse> GetStructuredPreviewAsync(
        string runId,
        int maxCells,
        CancellationToken cancellationToken);
}
