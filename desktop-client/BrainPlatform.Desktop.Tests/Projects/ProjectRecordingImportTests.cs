using System.Net;
using BrainPlatform.Desktop.Modules.Projects.Api;
using BrainPlatform.Desktop.Modules.Projects.Domain;

namespace BrainPlatform.Desktop.Tests.Projects;

public sealed class ProjectRecordingImportTests
{
    [Fact]
    public async Task ImportUsesSelectedProjectAndSubjectThenRefreshesOnlyItsImportedRows()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-import-vm", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var client = new FakeClient();
        var workspace = await CreateWorkspaceAsync(root, client);
        var progressValues = new List<long>();

        var result = await workspace.ImportRecordingAsync("test.edf", "subject-1", null,
            new InlineProgress(progressValues.Add), CancellationToken.None);

        Assert.Equal("project-one", client.LastImportProjectId);
        Assert.Equal("subject-1", client.LastSubjectId);
        Assert.Equal("project-one", Assert.Single(client.ListedSessionProjects));
        var imported = Assert.Single(workspace.Recordings, row => row.SourceKind == "backend");
        Assert.Equal(result.RecordingId, imported.RecordingId);
        Assert.Equal("session-one", imported.SessionId);
        Assert.Same(imported, workspace.SelectedRecording);
        Assert.Equal(65536, Assert.Single(progressValues));
    }

    [Fact]
    public async Task SubjectLookupResolvesAndScopesToSelectedDesktopProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-import-subject", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var client = new FakeClient();
        var workspace = await CreateWorkspaceAsync(root, client);

        var subjects = await workspace.ListSubjectsAsync(CancellationToken.None);

        Assert.Equal("project-one", Assert.Single(client.ListedSubjectProjects));
        Assert.Equal("subject-1", Assert.Single(subjects).SubjectId);
    }

    [Fact]
    public async Task ImportCanCreateANewNonIdentifyingSubjectCode()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-import-new-subject", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var client = new FakeClient();
        var workspace = await CreateWorkspaceAsync(root, client);

        await workspace.ImportRecordingAsync("test.edf", null, "SUBJECT-NEW", null, CancellationToken.None);

        Assert.Null(client.LastSubjectId);
        Assert.Equal("SUBJECT-NEW", client.LastNewSubjectLocalCode);
        Assert.Equal("project-one", client.LastImportProjectId);
        Assert.Contains(workspace.Recordings, row => row.SourceKind == "backend");
    }

    [Fact]
    public async Task DeleteImportedRecordingUsesBackendSessionAndRemovesProjectRow()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-import-delete", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var client = new FakeClient();
        var workspace = await CreateWorkspaceAsync(root, client);
        await workspace.RefreshImportedRecordingsAsync();
        var imported = Assert.Single(workspace.Recordings, row => row.SourceKind == "backend");

        await workspace.DeleteRecordingAsync(imported);

        Assert.Equal(("project-one", "session-one"), (client.LastDeleteProjectId, client.LastDeleteSessionId));
        Assert.DoesNotContain(imported, workspace.Recordings);
        Assert.DoesNotContain(imported, workspace.CompletedRecordings);
    }

    [Fact]
    public async Task StaleProjectRefreshCannotAddRowsAfterTheUserSwitchesProjects()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-import-project-race", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var client = new FakeClient();
        var store = new ResearchProjectStore(Path.Combine(root, "projects.json"));
        foreach (var id in new[] { "project-one", "project-two" })
        {
            var directory = Path.Combine(root, id);
            Directory.CreateDirectory(Path.Combine(directory, "recordings"));
            await store.SaveAsync(new ResearchProject(id, $"P-{id}", id, "", "", [], "tester", directory,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), CancellationToken.None);
        }
        var workspace = new ProjectWorkspaceViewModel(new OperationNotificationCenter(), store, backendClient: client);
        await workspace.RefreshAsync();
        var selected = workspace.Projects.Single(project => project.Id == "project-one");
        var switched = workspace.Projects.Single(project => project.Id == "project-two");
        workspace.SelectedProject = selected;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ListSessions = async (projectId, token) =>
        {
            await release.Task.WaitAsync(token);
            return [new BackendResearchSession("session-one", projectId, "subject-1", "recording-one", null, "", "")];
        };

        var refresh = workspace.RefreshImportedRecordingsAsync();
        workspace.SelectedProject = switched;
        release.SetResult();
        await refresh;

        Assert.Empty(workspace.Recordings);
    }

    [Fact]
    public async Task CancellationAndStructuredFailureDoNotPublishImportedRows()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-import-failures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var client = new FakeClient();
        var workspace = await CreateWorkspaceAsync(root, client);
        client.Import = (_, _, _, _, _, _, token) => Task.FromCanceled<BackendImportedRecording>(token);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workspace.ImportRecordingAsync(
            "test.edf", "subject-1", null, null, cancellation.Token));
        Assert.Empty(workspace.Recordings);

        client.Import = (_, _, _, _, _, _, _) => Task.FromException<BackendImportedRecording>(
            new ResearchProjectApiException(HttpStatusCode.UnprocessableEntity, "EDF_INVALID", "文件无效"));
        var error = await Assert.ThrowsAsync<ResearchProjectApiException>(() => workspace.ImportRecordingAsync(
            "test.edf", "subject-1", null, null, CancellationToken.None));

        Assert.Equal("EDF_INVALID", error.Code);
        Assert.Empty(workspace.Recordings);
    }

    private static async Task<ProjectWorkspaceViewModel> CreateWorkspaceAsync(string root, FakeClient client)
    {
        var projectDirectory = Path.Combine(root, "project-one");
        Directory.CreateDirectory(Path.Combine(projectDirectory, "recordings"));
        var store = new ResearchProjectStore(Path.Combine(root, "projects.json"));
        var project = new ResearchProject("project-one", "P001", "研究项目", "", "", [], "tester",
            projectDirectory, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await store.SaveAsync(project, CancellationToken.None);
        var workspace = new ProjectWorkspaceViewModel(new OperationNotificationCenter(), store, backendClient: client);
        await workspace.RefreshAsync();
        return workspace;
    }

    private sealed class FakeClient : IResearchProjectClient
    {
        public string? LastImportProjectId { get; private set; }
        public string? LastSubjectId { get; private set; }
        public string? LastNewSubjectLocalCode { get; private set; }
        public List<string> ListedSessionProjects { get; } = [];
        public List<string> ListedSubjectProjects { get; } = [];
        public string? LastDeleteProjectId { get; private set; }
        public string? LastDeleteSessionId { get; private set; }
        public Func<string, CancellationToken, Task<IReadOnlyList<BackendResearchSession>>> ListSessions { get; set; } =
            (projectId, _) => Task.FromResult<IReadOnlyList<BackendResearchSession>>([
                new("session-one", projectId, "subject-1", "recording-one", null, "", "")]);
        public Func<string, string, string?, string?, string?, IProgress<long>?, CancellationToken, Task<BackendImportedRecording>> Import { get; set; } =
            (_, _, _, _, _, progress, _) =>
            {
                progress?.Report(65536);
                return Task.FromResult(new BackendImportedRecording("project-one", "session-one", "subject-1",
                    "recording-one", "test.edf", ".edf", "2026-11-24T00:00:00Z", 512, 10,
                    ["F3"], ["eeg"], ["V"], new string('a', 64), 65536));
            };

        public Task<BackendResearchProject> EnsureProjectAsync(string projectId, string name, string description, CancellationToken cancellationToken) =>
            Task.FromResult(new BackendResearchProject(projectId, name, description, "", ""));
        public Task<IReadOnlyList<BackendResearchSubject>> ListSubjectsAsync(string projectId, CancellationToken cancellationToken)
        {
            ListedSubjectProjects.Add(projectId);
            return Task.FromResult<IReadOnlyList<BackendResearchSubject>>([new("subject-1", projectId, "S-001", "")]);
        }
        public Task<BackendResearchSubject> CreateSubjectAsync(string projectId, string localCode, CancellationToken cancellationToken) =>
            Task.FromResult(new BackendResearchSubject("subject-new", projectId, localCode, ""));
        public Task<IReadOnlyList<BackendResearchSession>> ListSessionsAsync(string projectId, CancellationToken cancellationToken)
        {
            ListedSessionProjects.Add(projectId);
            return ListSessions(projectId, cancellationToken);
        }
        public Task<BackendResearchSession> CreateSessionAsync(string projectId, CreateResearchSessionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<BackendProjectRecordingDeletion> DeleteImportedRecordingAsync(string projectId, string sessionId, CancellationToken cancellationToken)
        {
            LastDeleteProjectId = projectId;
            LastDeleteSessionId = sessionId;
            return Task.FromResult(new BackendProjectRecordingDeletion(sessionId, "recording-one", true));
        }
        public Task<BackendImportedRecording> ImportRecordingAsync(string projectId, string filePath, string? subjectId,
            string? newSubjectLocalCode, string? channelConfigurationSnapshotJson, IProgress<long>? progress,
            CancellationToken cancellationToken)
        {
            LastImportProjectId = projectId;
            LastSubjectId = subjectId;
            LastNewSubjectLocalCode = newSubjectLocalCode;
            return Import(projectId, filePath, subjectId, newSubjectLocalCode, channelConfigurationSnapshotJson,
                progress, cancellationToken);
        }
        public Task<BackendRecording> GetRecordingAsync(string recordingId, CancellationToken cancellationToken) =>
            Task.FromResult(new BackendRecording(recordingId, "test.edf", ".edf", "2026-11-24T00:00:00Z", 512, 10,
                ["F3"], ["eeg"], ["V"], new string('a', 64), 65536));
        public Task<BackendRawSampleRange> ReadRawSamplesAsync(string recordingId, long startSample, long endSample, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InlineProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
