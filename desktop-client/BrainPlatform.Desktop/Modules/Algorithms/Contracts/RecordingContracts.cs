using System.Text.Json.Serialization;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

public sealed record WpfRecordingRegistrationRequest(
    [property: JsonPropertyName("source_directory")] string SourceDirectory,
    [property: JsonPropertyName("signal_mode")] string SignalMode = "acquisition_montage",
    [property: JsonPropertyName("analysis_montage_profiles")] IReadOnlyList<MontageProfile>? AnalysisMontageProfiles = null);

public sealed record RegisteredRecording(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("original_name")] string OriginalName,
    [property: JsonPropertyName("extension")] string Extension,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("sfreq")] double? SamplingRateHz,
    [property: JsonPropertyName("duration_s")] double? DurationSeconds,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels,
    [property: JsonPropertyName("source_sha256")] string? SourceSha256,
    [property: JsonPropertyName("montage_catalogue")] IReadOnlyList<HistoricalMontageDescriptor>? MontageCatalogue = null);

public sealed record HistoricalMontageOutput(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("unavailable_reason")] string? UnavailableReason);

public sealed record HistoricalMontageDescriptor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("fingerprint")] string Fingerprint,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("is_acquisition_default")] bool IsAcquisitionDefault,
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("unavailable_reason")] string? UnavailableReason,
    [property: JsonPropertyName("outputs")] IReadOnlyList<HistoricalMontageOutput> Outputs,
    [property: JsonPropertyName("source_profile_id")] string? SourceProfileId = null);
