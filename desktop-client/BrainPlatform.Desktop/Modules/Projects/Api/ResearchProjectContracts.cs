using System.Text.Json.Serialization;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

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

public sealed record BackendProjectRecordingDeletion(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("recording_deleted")] bool RecordingDeleted);

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
    [property: JsonPropertyName("file_size_bytes")] long FileSizeBytes,
    [property: JsonPropertyName("analysis_channels")] IReadOnlyList<string>? AnalysisChannels = null,
    [property: JsonPropertyName("import_channel_configuration_snapshot_json")] string? ImportChannelConfigurationSnapshotJson = null,
    [property: JsonPropertyName("import_channel_column_mapping_json")] string? ImportChannelColumnMappingJson = null,
    [property: JsonPropertyName("import_acquisition_montage_snapshot_json")] string? ImportAcquisitionMontageSnapshotJson = null,
    [property: JsonPropertyName("import_montage_configuration_snapshot_json")] string? ImportMontageConfigurationSnapshotJson = null,
    [property: JsonPropertyName("import_montage_catalogue_snapshot_json")] string? ImportMontageCatalogueSnapshotJson = null);

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
    [property: JsonPropertyName("file_size_bytes")] long? FileSizeBytes,
    [property: JsonPropertyName("import_channel_configuration_snapshot_json")] string? ImportChannelConfigurationSnapshotJson = null,
    [property: JsonPropertyName("import_channel_column_mapping_json")] string? ImportChannelColumnMappingJson = null,
    [property: JsonPropertyName("import_acquisition_montage_snapshot_json")] string? ImportAcquisitionMontageSnapshotJson = null,
    [property: JsonPropertyName("import_montage_configuration_snapshot_json")] string? ImportMontageConfigurationSnapshotJson = null,
    [property: JsonPropertyName("import_montage_catalogue_snapshot_json")] string? ImportMontageCatalogueSnapshotJson = null,
    [property: JsonPropertyName("analysis_channels")] IReadOnlyList<string>? AnalysisChannels = null,
    [property: JsonPropertyName("montage_catalogue")] IReadOnlyList<HistoricalMontageDescriptor>? MontageCatalogue = null)
{
    public RegisteredRecording ToAnalysisDescriptor() => new(
        Id, OriginalName, Extension, CreatedAt, SamplingRateHz, DurationSeconds,
        AnalysisChannels ?? Channels.Where((_, index) =>
            string.Equals(ChannelTypes.ElementAtOrDefault(index), "eeg", StringComparison.OrdinalIgnoreCase)).ToArray(),
        SourceSha256, MontageCatalogue);
}

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
