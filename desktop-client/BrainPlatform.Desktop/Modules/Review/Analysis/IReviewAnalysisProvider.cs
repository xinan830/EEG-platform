using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>
/// Loads an already completed algorithm result for one review cursor.
/// Providers do not own playback, calculate science, or touch WPF controls.
/// </summary>
public interface IReviewAnalysisProvider
{
    string AlgorithmId { get; }

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
}
