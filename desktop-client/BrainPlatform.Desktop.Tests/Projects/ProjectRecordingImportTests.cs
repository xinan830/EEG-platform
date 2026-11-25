using System.Net;
using BrainPlatform.Desktop.Modules.Projects.Api;
using BrainPlatform.Desktop.Modules.Projects.Domain;

namespace BrainPlatform.Desktop.Tests.Projects;

public sealed class ProjectRecordingImportTests
{
    [Fact]
    public async Task OptionalSubjectImportAndEditRefreshTheList()
    {
        var root = Directory.CreateTempSubdirectory("subject-import-edit-").FullName;
        try
        {
            var client = new FakeClient();
            var workspace = await CreateWorkspaceAsync(root, client);
            await workspace.ImportRecordingAsync("test.edf", null, null, null, CancellationToken.None, "仅姓名");
            Assert.Null(client.LastSubjectId);
            Assert.Null(client.LastNewSubjectLocalCode);
            Assert.Equal("仅姓名", client.LastNewSubjectName);
            await workspace.EditRecordingAsync(Assert.Single(workspace.Recordings), "record", " S001 ", " 姓名 ");
            var row = Assert.Single(workspace.Recordings);
            Assert.Equal("S001 · 姓名", row.SubjectText);
            Assert.Equal("record", row.Name);
            Assert.Same(row, Assert.Single(workspace.CompletedRecordings));
            await workspace.EditRecordingAsync(row, "record", "", "");
            Assert.Equal("未关联", Assert.Single(workspace.Recordings).SubjectText);
        }
        finally { Directory.Delete(root, true); }
    }

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
    public async Task RenameImportedRecordingPersistsThroughRefreshAndRetainsSourceIdentity()
    {
        var root = Directory.CreateTempSubdirectory("import-rename-").FullName;
        try
        {
            var client = new FakeClient();
            var workspace = await CreateWorkspaceAsync(root, client);
            await workspace.RefreshImportedRecordingsAsync();
            var original = Assert.Single(workspace.Recordings);
            workspace.SelectedRecording = original;
            Assert.True(original.CanRename);

            await workspace.RenameRecordingAsync(original, "  静息复测  ");

            var renamed = Assert.Single(workspace.Recordings);
            Assert.Equal(original with { Name = "静息复测" }, renamed);
            Assert.Same(renamed, workspace.SelectedRecording);
            Assert.Same(renamed, Assert.Single(workspace.CompletedRecordings));
            Assert.Equal(("project-one", "session-one"), client.LastRename);
            workspace.RefreshRecordings();
            await workspace.RefreshImportedRecordingsAsync();
            Assert.Equal("静息复测", Assert.Single(workspace.Recordings).Name);
            Assert.Equal("recording-one", Assert.Single(workspace.Recordings).RecordingId);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task InvalidOrFailedImportedRenameKeepsTheOriginalRow()
    {
        var root = Directory.CreateTempSubdirectory("import-rename-fail-").FullName;
        try
        {
            var client = new FakeClient();
            var workspace = await CreateWorkspaceAsync(root, client);
            await workspace.RefreshImportedRecordingsAsync();
            var original = Assert.Single(workspace.Recordings);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.RenameRecordingAsync(original, " \t"));
            Assert.Null(client.LastRename);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.RenameRecordingAsync(
                original with { SessionId = "another-project" }, "invalid"));
            Assert.Null(client.LastRename);
            client.RenameFailure = new ResearchProjectApiException(HttpStatusCode.NotFound, "RECORDING_SESSION_NOT_FOUND", "记录不存在");
            await Assert.ThrowsAsync<ResearchProjectApiException>(() => workspace.RenameRecordingAsync(original, "new name"));
            Assert.Same(original, Assert.Single(workspace.Recordings));
            Assert.Same(original, Assert.Single(workspace.CompletedRecordings));
        }
        finally { Directory.Delete(root, recursive: true); }
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

    [Fact]
    public async Task RefactoredRenameAndImportDialogsLoadXamlResourcesAndLayoutCleanly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"eeg-dialog-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var edfPath = Path.Combine(root, "recording-review.edf");
            await File.WriteAllBytesAsync(edfPath, new byte[2048]);
            var workspace = await CreateWorkspaceAsync(root, new FakeClient());

            BrainPlatform.Desktop.Tests.Shared.WpfTestApplication.Run(() =>
            {
                var rename = new BrainPlatform.Desktop.Modules.Projects.Views.RenameRecordingDialog("  545456  ", "S001", "姓名");
                var renameContent = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(rename.Content);
                renameContent.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.Equal("545456", rename.RecordingName);
                Assert.Equal("S001", rename.SubjectCode);
                Assert.Equal("姓名", rename.SubjectName);
                Assert.True(renameContent.DesiredSize.Width >= 440);
                Assert.True(renameContent.DesiredSize.Height >= 220);

                var import = new BrainPlatform.Desktop.Modules.Projects.Views.RecordingImportDialog(workspace, edfPath);
                var importContent = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(import.Content);
                importContent.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.True(importContent.DesiredSize.Width >= 496);
                Assert.True(importContent.DesiredSize.Height >= 320);
                var subjects = Assert.IsType<System.Windows.Controls.ComboBox>(import.FindName("SubjectsComboBox"));
                Assert.Equal("不关联受试者", Assert.IsType<BackendResearchSubject>(subjects.SelectedItem).DisplayText);
                subjects.SelectedIndex = 1;
                var nameInput = Assert.IsType<System.Windows.Controls.TextBox>(import.FindName("NewNameBox"));
                Assert.False(nameInput.IsReadOnly);
                nameInput.Text = "仅姓名";
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
        public string? LastNewSubjectName { get; private set; }
        public List<string> ListedSessionProjects { get; } = [];
        public List<string> ListedSubjectProjects { get; } = [];
        public string? LastDeleteProjectId { get; private set; }
        public string? LastDeleteSessionId { get; private set; }
        public (string, string)? LastRename { get; private set; }
        public Exception? RenameFailure { get; set; }
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
        public Task<BackendResearchSession> RenameSessionAsync(string projectId, string sessionId, string name, CancellationToken cancellationToken)
        {
            LastRename = (projectId, sessionId);
            if (RenameFailure is not null) return Task.FromException<BackendResearchSession>(RenameFailure);
            var renamed = new BackendResearchSession(sessionId, projectId, "subject-1", "recording-one", null, name, "");
            ListSessions = (_, _) => Task.FromResult<IReadOnlyList<BackendResearchSession>>([renamed]);
            return Task.FromResult(renamed);
        }
        public Task<BackendProjectRecordingDeletion> DeleteImportedRecordingAsync(string projectId, string sessionId, CancellationToken cancellationToken)
            => DeleteCore(projectId, sessionId);

        public Task<BackendResearchSession> EditSessionAsync(string projectId, string sessionId, EditResearchSessionRequest request, CancellationToken cancellationToken)
        {
            var session = new BackendResearchSession(sessionId, projectId, "subject-1", "recording-one", null,
                request.Label, "", request.SubjectCode, request.SubjectName);
            ListSessions = (_, _) => Task.FromResult<IReadOnlyList<BackendResearchSession>>([session]);
            return Task.FromResult(session);
        }

        private Task<BackendProjectRecordingDeletion> DeleteCore(string projectId, string sessionId)
        {
            LastDeleteProjectId = projectId;
            LastDeleteSessionId = sessionId;
            return Task.FromResult(new BackendProjectRecordingDeletion(sessionId, "recording-one", true));
        }
        public Task<BackendImportedRecording> ImportRecordingAsync(string projectId, string filePath, string? subjectId,
            string? newSubjectLocalCode, string? channelConfigurationSnapshotJson, IProgress<long>? progress,
            CancellationToken cancellationToken, string? newSubjectName = null)
        {
            LastNewSubjectName = newSubjectName;
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
