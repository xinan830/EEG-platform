using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

public interface IPreprocessingClient
{
    Task<ArtifactReferenceCatalogue> SaveChannelIdentitiesAsync(string recordingId, ChannelIdentityUpdate update,
        CancellationToken cancellationToken = default) => throw new NotSupportedException("当前后端客户端不支持保存通道身份。");

    Task<PreprocessingAnalysisChannels> GetPreprocessingAnalysisChannelsAsync(string recordingId,
        OfflinePreprocessingConfigPayload preprocessing, string? montageId = null,
        CancellationToken cancellationToken = default);

    Task<ArtifactReferenceCatalogue> GetArtifactReferenceChannelsAsync(string recordingId,
        CancellationToken cancellationToken = default);
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
        IReadOnlyList<string>? eogChannels = null,
        IReadOnlyList<string>? ecgChannels = null,
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
        IReadOnlyList<string>? eogChannels = null,
        IReadOnlyList<string>? ecgChannels = null,
        CancellationToken cancellationToken = default);

    Task<PreprocessingPreviewWindowResponse> PreviewPreprocessedWindowAsync(
        string recordingId,
        PreprocessingPreviewWindowRequest request,
        CancellationToken cancellationToken = default);
}
