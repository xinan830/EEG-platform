using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>
/// Maps review targets to algorithm-owned requests and typed result projections.
/// Adapters do not own playback, calculate science, or touch WPF controls.
/// </summary>
public interface IReviewAlgorithmAdapter
{
    string AlgorithmId { get; }

    string DisplayName => AlgorithmId;

    /// <summary>Explicit opt-in for backend-planned dynamic review windows.</summary>
    bool SupportsOnDemandWindows => false;

    Task<RegisteredRecording> RegisterAsync(string directory, CancellationToken cancellationToken,
        IReadOnlyList<MontageProfile>? montageProfiles = null);

    Task<AlgorithmCatalogItem> GetAlgorithmAsync(CancellationToken cancellationToken);

    Task<AnalysisRunResponse> EnsureRunAsync(
        AnalysisRunRequest request,
        Action<AnalysisRunResponse>? onStatusChanged,
        CancellationToken cancellationToken);

    Task<WindowPreviewResponse> LoadAsync(
        string recordingId,
        string runId,
        long cursorSample,
        CancellationToken cancellationToken);

    Task<WindowPreviewResponse> LoadOnDemandAsync(
        string recordingId,
        AnalysisRunRequest request,
        long cursorSample,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException($"算法 {AlgorithmId} 不支持按需动态回溯窗口。");

    Task ReleaseOnDemandAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task<WindowPreviewResponse> LoadOnDemandAsync(string recordingId, AnalysisRunRequest request,
        long cursorSample, CancellationToken cancellationToken, IProgress<DynamicWindowResponse>? progress) =>
        LoadOnDemandAsync(recordingId, request, cursorSample, cancellationToken);

    Task<WindowPreviewResponse> LoadOnDemandAsync(string recordingId, ReviewAnalysisSnapshot snapshot,
        long cursorSample, CancellationToken cancellationToken, IProgress<DynamicWindowResponse>? progress) =>
        LoadOnDemandAsync(recordingId, snapshot.ToRunRequest(), cursorSample, cancellationToken, progress);
}
