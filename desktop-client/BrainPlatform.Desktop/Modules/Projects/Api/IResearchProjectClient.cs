namespace BrainPlatform.Desktop.Modules.Projects.Api;

public interface IResearchProjectClient
{
    Task<BackendResearchProject> EnsureProjectAsync(string projectId, string name, string description, CancellationToken cancellationToken);
    Task<IReadOnlyList<BackendResearchSubject>> ListSubjectsAsync(string projectId, CancellationToken cancellationToken);
    Task<BackendResearchSubject> CreateSubjectAsync(string projectId, string localCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<BackendResearchSession>> ListSessionsAsync(string projectId, CancellationToken cancellationToken);
    Task<BackendResearchSession> CreateSessionAsync(string projectId, CreateResearchSessionRequest request, CancellationToken cancellationToken);
    Task<BackendResearchSession> RenameSessionAsync(string projectId, string sessionId, string name, CancellationToken cancellationToken);
    Task<BackendResearchSession> EditSessionAsync(string projectId, string sessionId, EditResearchSessionRequest request, CancellationToken cancellationToken);
    Task<BackendProjectRecordingDeletion> DeleteImportedRecordingAsync(string projectId, string sessionId, CancellationToken cancellationToken);
    Task<BackendImportedRecording> ImportRecordingAsync(string projectId, string filePath, string? subjectId,
        string? newSubjectLocalCode, string? channelConfigurationSnapshotJson, IProgress<long>? progress,
        CancellationToken cancellationToken, string? newSubjectName = null);
    Task<BackendRecording> GetRecordingAsync(string recordingId, CancellationToken cancellationToken);
    Task<BackendRawSampleRange> ReadRawSamplesAsync(string recordingId, long startSample, long endSample,
        CancellationToken cancellationToken);
}
