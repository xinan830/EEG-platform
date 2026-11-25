using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

public interface IPreprocessingClient
{
    Task<BadChannelInspectionResponse> InspectBadChannelsAsync(
        string recordingId,
        double startSeconds = 0,
        double durationSeconds = 180,
        CancellationToken cancellationToken = default);

    Task<ArtifactSegmentInspectionResponse> InspectArtifactSegmentsAsync(
        string recordingId,
        double startSeconds = 0,
        double durationSeconds = 180,
        string? montageId = null,
        double? peakToPeakUv = 300,
        double? muscleZScoreThreshold = 4.0,
        IReadOnlyList<string>? badChannels = null,
        string badChannelPolicy = "none",
        CancellationToken cancellationToken = default);

    Task<IcaInspectionResponse> InspectIcaComponentsAsync(
        string recordingId,
        string? montageId = null,
        double lowHz = 1.0,
        double highHz = 50.0,
        double? notchHz = 50.0,
        IReadOnlyList<string>? badChannels = null,
        string badChannelPolicy = "none",
        string artifactPolicy = "none",
        double? peakToPeakUv = 300,
        double? muscleZScoreThreshold = 4.0,
        bool allowFrontalEogProxy = true,
        int? icaComponentCount = null,
        double icaEogThreshold = 0.7,
        CancellationToken cancellationToken = default);

    Task<PreprocessingPreviewWindowResponse> PreviewPreprocessedWindowAsync(
        string recordingId,
        PreprocessingPreviewWindowRequest request,
        CancellationToken cancellationToken = default);
}
