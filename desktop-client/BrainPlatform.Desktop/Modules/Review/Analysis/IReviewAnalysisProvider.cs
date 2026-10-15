using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>
/// Loads an already completed algorithm result for one review cursor.
/// Providers do not own playback, calculate science, or touch WPF controls.
/// </summary>
public interface IReviewAnalysisProvider
{
    string AlgorithmId { get; }

    /// <summary>Opt-in capability; disabled providers keep the legacy Run path.</summary>
    bool SupportsOnDemandWindows => false;

    Task<RegisteredRecording> RegisterAsync(string directory, CancellationToken cancellationToken);

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
        throw new NotSupportedException("当前分析 provider 不支持按需动态窗口。");

    Task ReleaseOnDemandAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task<WindowPreviewResponse> LoadOnDemandAsync(string recordingId, AnalysisRunRequest request,
        long cursorSample, CancellationToken cancellationToken, IProgress<DynamicWindowResponse>? progress) =>
        LoadOnDemandAsync(recordingId, request, cursorSample, cancellationToken);
}
