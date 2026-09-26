using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

public sealed record WpfRecordingRegistrationRequest(
    [property: JsonPropertyName("source_directory")] string SourceDirectory);

public sealed record RegisteredRecording(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("original_name")] string OriginalName,
    [property: JsonPropertyName("extension")] string Extension,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("sfreq")] double? SamplingRateHz,
    [property: JsonPropertyName("duration_s")] double? DurationSeconds,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels,
    [property: JsonPropertyName("source_sha256")] string? SourceSha256);
