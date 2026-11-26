using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

public sealed record ArtifactReferenceChannel(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("identity")] string Identity = "unconfirmed",
    [property: JsonPropertyName("identity_source")] string IdentitySource = "unconfirmed");

public sealed record ArtifactReferenceCatalogue(
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("sampling_rate_hz")] double SamplingRateHz,
    [property: JsonPropertyName("sample_count")] long SampleCount,
    [property: JsonPropertyName("channels")] IReadOnlyList<ArtifactReferenceChannel> Channels,
    [property: JsonPropertyName("source_sha256")] string SourceSha256 = "");

public sealed record ChannelIdentitySelection(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("identity")] string Identity);

public sealed record ChannelIdentityUpdate(
    [property: JsonPropertyName("source_sha256")] string SourceSha256,
    [property: JsonPropertyName("channels")] IReadOnlyList<ChannelIdentitySelection> Channels);

public sealed record PreprocessingAnalysisChannels(
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels);
