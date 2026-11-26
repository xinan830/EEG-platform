using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

public sealed record ArtifactReferenceChannel(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("unit")] string Unit);

public sealed record ArtifactReferenceCatalogue(
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("sampling_rate_hz")] double SamplingRateHz,
    [property: JsonPropertyName("sample_count")] long SampleCount,
    [property: JsonPropertyName("channels")] IReadOnlyList<ArtifactReferenceChannel> Channels);

public sealed record PreprocessingAnalysisChannels(
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels);
