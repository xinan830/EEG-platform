using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Projects.Api;

public sealed record EnsureDesktopProjectRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description);

public sealed record BackendResearchProject(
    [property: JsonPropertyName("project_id")] string ProjectId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("updated_at")] string UpdatedAt);

public sealed record CreateSubjectRequest([property: JsonPropertyName("local_code")] string LocalCode);

public sealed record BackendResearchSubject(
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("project_id")] string ProjectId,
    [property: JsonPropertyName("local_code")] string LocalCode,
    [property: JsonPropertyName("created_at")] string CreatedAt);

public sealed record CreateResearchSessionRequest(
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("condition_id")] string? ConditionId = null,
    [property: JsonPropertyName("label")] string Label = "");

public sealed record BackendResearchSession(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("project_id")] string ProjectId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("condition_id")] string? ConditionId,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("created_at")] string CreatedAt);

public sealed record BackendImportedRecording(
    [property: JsonPropertyName("project_id")] string ProjectId,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("original_name")] string OriginalName,
    [property: JsonPropertyName("extension")] string Extension,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("sfreq")] double SamplingRateHz,
    [property: JsonPropertyName("duration_s")] double DurationSeconds,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels,
    [property: JsonPropertyName("channel_types")] IReadOnlyList<string> ChannelTypes,
    [property: JsonPropertyName("channel_units")] IReadOnlyList<string> ChannelUnits,
    [property: JsonPropertyName("source_sha256")] string SourceSha256,
    [property: JsonPropertyName("file_size_bytes")] long FileSizeBytes);

public sealed record BackendRecording(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("original_name")] string OriginalName,
    [property: JsonPropertyName("extension")] string Extension,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("sfreq")] double? SamplingRateHz,
    [property: JsonPropertyName("duration_s")] double? DurationSeconds,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels,
    [property: JsonPropertyName("channel_types")] IReadOnlyList<string> ChannelTypes,
    [property: JsonPropertyName("channel_units")] IReadOnlyList<string> ChannelUnits,
    [property: JsonPropertyName("source_sha256")] string? SourceSha256,
    [property: JsonPropertyName("file_size_bytes")] long? FileSizeBytes);

public sealed record BackendRawSampleRange(
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("start_sample")] long StartSample,
    [property: JsonPropertyName("end_sample")] long EndSample,
    [property: JsonPropertyName("sampling_rate_hz")] double SamplingRateHz,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels,
    [property: JsonPropertyName("channel_units")] IReadOnlyList<string> ChannelUnits,
    [property: JsonPropertyName("values_v")] IReadOnlyList<IReadOnlyList<double?>> ValuesVolts,
    [property: JsonPropertyName("unavailable_intervals")] IReadOnlyList<BackendUnavailableInterval> UnavailableIntervals);

public sealed record BackendUnavailableInterval(
    [property: JsonPropertyName("start_sample")] long StartSample,
    [property: JsonPropertyName("end_sample")] long EndSample,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels);
