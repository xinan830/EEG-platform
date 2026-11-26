using System.Globalization;
using System.Net.Http;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

public sealed partial class HttpAlgorithmClient : IPreprocessingClient
{
    public Task<PreprocessingAnalysisChannels> GetPreprocessingAnalysisChannelsAsync(string recordingId,
        OfflinePreprocessingConfigPayload preprocessing, string? montageId = null,
        CancellationToken cancellationToken = default) => SendAsync<PreprocessingAnalysisChannels>(HttpMethod.Post,
            $"api/recordings/{Uri.EscapeDataString(RequireId(recordingId))}/preprocessing/analysis-channels" +
            (montageId is null ? "" : $"?montage_id={Uri.EscapeDataString(montageId)}"), preprocessing, cancellationToken);

    public Task<ArtifactReferenceCatalogue> GetArtifactReferenceChannelsAsync(string recordingId,
        CancellationToken cancellationToken = default) => SendAsync<ArtifactReferenceCatalogue>(HttpMethod.Get,
            $"api/recordings/{Uri.EscapeDataString(RequireId(recordingId))}/preprocessing/reference-channels",
            null, cancellationToken);
    public Task<BadChannelInspectionResponse> InspectBadChannelsAsync(
        string recordingId,
        double startSeconds = 0,
        double durationSeconds = 180,
        CancellationToken cancellationToken = default)
    {
        var query = $"start_s={Fmt(startSeconds)}&duration_s={Fmt(Math.Clamp(durationSeconds, 1, 600))}";
        return SendAsync<BadChannelInspectionResponse>(
            HttpMethod.Get,
            $"api/recordings/{Uri.EscapeDataString(RequireId(recordingId))}/preprocessing/bad-channels?{query}",
            null,
            cancellationToken);
    }

    public Task<ArtifactSegmentInspectionResponse> InspectArtifactSegmentsAsync(
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
        CancellationToken cancellationToken = default)
    {
        var parts = new List<string>
        {
            $"start_s={Fmt(startSeconds)}",
            $"duration_s={Fmt(Math.Clamp(durationSeconds, 1, 600))}",
            $"bad_channel_policy={Uri.EscapeDataString(badChannelPolicy)}",
            "low_hz=1&high_hz=50&notch_hz=50&notch_q=30",
        };
        if (!string.IsNullOrWhiteSpace(montageId))
            parts.Add($"montage_id={Uri.EscapeDataString(montageId)}");
        if (peakToPeakUv is > 0)
            parts.Add($"peak_to_peak_uv={Fmt(peakToPeakUv.Value)}");
        if (muscleZScoreThreshold is > 0)
            parts.Add($"muscle_zscore_threshold={Fmt(muscleZScoreThreshold.Value)}");
        if (badChannels is { Count: > 0 })
            parts.Add($"bad_channels={Uri.EscapeDataString(string.Join(",", badChannels))}");
        if (eogChannels is { Count: > 0 })
            parts.Add($"eog_channels={Uri.EscapeDataString(string.Join(",", eogChannels))}");
        if (ecgChannels is { Count: > 0 })
            parts.Add($"ecg_channels={Uri.EscapeDataString(string.Join(",", ecgChannels))}");

        return SendAsync<ArtifactSegmentInspectionResponse>(
            HttpMethod.Get,
            $"api/recordings/{Uri.EscapeDataString(RequireId(recordingId))}/preprocessing/artifact-segments?{string.Join("&", parts)}",
            null,
            cancellationToken);
    }

    public Task<IcaInspectionResponse> InspectIcaComponentsAsync(
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
        CancellationToken cancellationToken = default)
    {
        var parts = new List<string>
        {
            $"low_hz={Fmt(lowHz)}",
            $"high_hz={Fmt(highHz)}",
            $"bad_channel_policy={Uri.EscapeDataString(badChannelPolicy)}",
            $"artifact_policy={Uri.EscapeDataString(artifactPolicy)}",
            $"allow_frontal_eog_proxy={(allowFrontalEogProxy ? "true" : "false")}",
            $"ica_eog_threshold={Fmt(icaEogThreshold)}",
        };
        if (notchHz is > 0)
            parts.Add($"notch_hz={Fmt(notchHz.Value)}");
        if (!string.IsNullOrWhiteSpace(montageId))
            parts.Add($"montage_id={Uri.EscapeDataString(montageId)}");
        if (peakToPeakUv is > 0)
            parts.Add($"peak_to_peak_uv={Fmt(peakToPeakUv.Value)}");
        if (muscleZScoreThreshold is > 0)
            parts.Add($"muscle_zscore_threshold={Fmt(muscleZScoreThreshold.Value)}");
        if (badChannels is { Count: > 0 })
            parts.Add($"bad_channels={Uri.EscapeDataString(string.Join(",", badChannels))}");
        if (icaComponentCount is >= 2)
            parts.Add($"ica_n_components={icaComponentCount.Value}");
        if (eogChannels is { Count: > 0 })
            parts.Add($"eog_channels={Uri.EscapeDataString(string.Join(",", eogChannels))}");
        if (ecgChannels is { Count: > 0 })
            parts.Add($"ecg_channels={Uri.EscapeDataString(string.Join(",", ecgChannels))}");

        return SendAsync<IcaInspectionResponse>(
            HttpMethod.Get,
            $"api/recordings/{Uri.EscapeDataString(RequireId(recordingId))}/preprocessing/ica-components?{string.Join("&", parts)}",
            null,
            cancellationToken);
    }

    public Task<PreprocessingPreviewWindowResponse> PreviewPreprocessedWindowAsync(
        string recordingId,
        PreprocessingPreviewWindowRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<PreprocessingPreviewWindowResponse>(
            HttpMethod.Post,
            $"api/recordings/{Uri.EscapeDataString(RequireId(recordingId))}/preprocessing/preview-window",
            request,
            cancellationToken);

    private static string Fmt(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
