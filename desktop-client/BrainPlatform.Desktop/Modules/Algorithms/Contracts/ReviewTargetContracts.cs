using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

/// <summary>
/// Cross-layer identity for one playback target. The playback context owns the
/// generation; consumers must not create a second clock or reinterpret time.
/// </summary>
public sealed record ReviewTargetEnvelope(
    [property: JsonPropertyName("trace_id")] string TraceId,
    [property: JsonPropertyName("generation")] long Generation,
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("algorithm_id")] string AlgorithmId,
    [property: JsonPropertyName("definition_id")] string? DefinitionId,
    [property: JsonPropertyName("definition_version")] string? DefinitionVersion,
    [property: JsonPropertyName("config_fingerprint")] string ConfigurationFingerprint,
    [property: JsonPropertyName("cursor_sample")] long CursorSample,
    [property: JsonPropertyName("sample_rate_hz")] double SampleRateHz,
    [property: JsonPropertyName("channel_order")] IReadOnlyList<string> ChannelOrder,
    [property: JsonPropertyName("lane")] ReviewTaskLane Lane,
    [property: JsonPropertyName("playback_delivery_id")] string? PlaybackDeliveryId = null)
{
    public string ScientificContextKey => JsonSerializer.Serialize(new
    {
        RecordingId, AlgorithmId, DefinitionId, DefinitionVersion,
        ConfigurationFingerprint, SampleRateHz, ChannelOrder
    });
}

public enum ReviewTaskLane
{
    WaveformPreview,
    WindowIndependentAnalysis,
    StatefulAnalysis,
    BatchAnalysis,
}
