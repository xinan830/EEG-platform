using System.Text.Json;
using BrainPlatform.Desktop.Modules.Projects.Api;

namespace BrainPlatform.Desktop.Tests.Projects;

public sealed class SubjectDirectoryTests
{
    [Fact]
    public async Task SharedSelectorSelectsExistingCreatesNameOnlyAndManagesStableIdentity()
    {
        using var fixture = new Fixture();
        var workspace = await fixture.CreateAsync();
        var selector = new SubjectSelectorViewModel();
        await selector.InitializeAsync(workspace, new(), CancellationToken.None);
        Assert.Equal(new RecordingSubject(), selector.Selection);
        selector.Selected = selector.Subjects.Single(item => item.SubjectId == "person");
        Assert.Equal(new RecordingSubject("S001", "原姓名", "person"), await selector.ResolveAsync(CancellationToken.None));
        Assert.Equal(0, fixture.Client.ResolveCount);
        selector.BeginEdit();
        Assert.Throws<InvalidOperationException>(() => selector.Selection);
        selector.Code = "S002";
        selector.Name = "新姓名";
        await selector.SaveEditAsync(CancellationToken.None);
        Assert.False(selector.IsEditing);
        Assert.Equal(new RecordingSubject("S002", "新姓名", "person"), selector.Selection);
        selector.BeginEdit();
        selector.Code = "未保存";
        selector.CancelEdit();
        Assert.Equal("S002", selector.Code);
        await selector.DeleteAsync(CancellationToken.None);
        Assert.Equal(new RecordingSubject(), selector.Selection);
        Assert.DoesNotContain(selector.Subjects, item => item.SubjectId == "person");
        selector.Selected = selector.Subjects[1];
        selector.Name = "仅姓名";
        var created = await selector.ResolveAsync(CancellationToken.None);
        Assert.Equal("仅姓名", created.Name);
        Assert.NotNull(created.SubjectId);
        Assert.Equal(created, await selector.ResolveAsync(CancellationToken.None));
        Assert.Equal(1, fixture.Client.ResolveCount);
    }

    [Fact]
    public async Task LegacyLocalSubjectBindsOnceThenRenameDeletePropagateWithoutChangingSamples()
    {
        using var fixture = new Fixture();
        var directory = fixture.CreateLocalRecording();
        await RecordingMetadataStore.SaveAsync(directory, "本地记录", new("S001", "原姓名"));
        var samples = await File.ReadAllBytesAsync(Path.Combine(directory, "samples-000001.bin"));
        var manifest = await File.ReadAllBytesAsync(Path.Combine(directory, "manifest.json"));
        var workspace = await fixture.CreateAsync();
        Assert.Contains(await workspace.ListSubjectsAsync(CancellationToken.None), item => item.SubjectId == "person");
        Assert.Equal("person", Assert.Single(workspace.Recordings).SubjectId);
        Assert.Equal(1, fixture.Client.ResolveCount);
        await workspace.UpdateSubjectAsync("project", "person", "S002", "新姓名", CancellationToken.None);
        Assert.Equal("S002 · 新姓名", Assert.Single(workspace.Recordings).SubjectText);
        Assert.Equal("person", RecordingMetadataStore.ReadSubject(directory).SubjectId);
        await workspace.DeleteSubjectAsync("project", "person", CancellationToken.None);
        Assert.Equal("未关联", Assert.Single(workspace.Recordings).SubjectText);
        Assert.Null(RecordingMetadataStore.ReadSubject(directory).SubjectId);
        await workspace.RefreshImportedRecordingsAsync();
        Assert.Equal(1, fixture.Client.ResolveCount);
        Assert.Equal(samples, await File.ReadAllBytesAsync(Path.Combine(directory, "samples-000001.bin")));
        Assert.Equal(manifest, await File.ReadAllBytesAsync(Path.Combine(directory, "manifest.json")));
        Assert.Equal("本地记录", Assert.Single(workspace.Recordings).Name);
    }

    [Fact]
    public async Task FailedDirectoryLoadPreservesInitialAssociationAndProjectChangesRejectMutations()
    {
        using var fixture = new Fixture();
        var workspace = await fixture.CreateAsync();
        var original = new RecordingSubject("S001", "原姓名", "person");
        var selector = new SubjectSelectorViewModel();
        fixture.Client.FailListing = true;
        await Assert.ThrowsAsync<IOException>(() => selector.InitializeAsync(workspace, original, CancellationToken.None));
        Assert.Equal(original, selector.Selection);
        fixture.Client.FailListing = false;
        await selector.InitializeAsync(workspace, original, CancellationToken.None);
        workspace.SelectedProject = workspace.SelectedProject! with { Id = "other-project" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => selector.DeleteAsync(CancellationToken.None));
        Assert.Single(fixture.Client.Subjects);
    }

    [Fact]
    public async Task OpeningLegacyNameOnlyRecordEditorSelectsTheMigratedIdentityInsteadOfCreatingAgain()
    {
        using var fixture = new Fixture();
        var directory = fixture.CreateLocalRecording();
        await RecordingMetadataStore.SaveAsync(directory, "record", new("", "仅姓名"));
        var workspace = await fixture.CreateAsync();
        var row = Assert.Single(workspace.Recordings);
        var selector = new SubjectSelectorViewModel();
        await selector.InitializeAsync(workspace, new(row.SubjectCode, row.SubjectName), CancellationToken.None, row);
        Assert.False(selector.IsNew);
        Assert.NotNull(selector.Selection.SubjectId);
        var resolved = await selector.ResolveAsync(CancellationToken.None);
        Assert.Equal(selector.Selection.SubjectId, resolved.SubjectId);
        Assert.Equal(1, fixture.Client.ResolveCount);
    }

    [Fact]
    public async Task SharedSelectorLoadsSexAndKeepsThisRecordingAgeAcrossSubjectEditDeleteAndNewSelection()
    {
        using var fixture = new Fixture();
        fixture.Client.Subjects[0] = fixture.Client.Subjects[0] with { Sex = "female" };
        var workspace = await fixture.CreateAsync();
        var selector = new SubjectSelectorViewModel();
        await selector.InitializeAsync(workspace, new("S001", "原姓名", "person", "female", 25), CancellationToken.None);
        Assert.Equal("female", selector.Sex);
        Assert.Equal(25, selector.Selection.AgeAtRecordingYears);
        selector.BeginEdit();
        selector.Sex = "male";
        selector.AgeText = "26";
        await selector.SaveEditAsync(CancellationToken.None);
        Assert.Equal("male", selector.Selection.Sex);
        Assert.Equal(26, selector.Selection.AgeAtRecordingYears);
        await selector.DeleteAsync(CancellationToken.None);
        Assert.Null(selector.Selection.SubjectId);
        Assert.Null(selector.Selection.Sex);
        Assert.Equal(26, selector.Selection.AgeAtRecordingYears);
        selector.Selected = selector.Subjects[1];
        selector.Name = "新受试者";
        selector.Sex = "other";
        var resolved = await selector.ResolveAsync(CancellationToken.None);
        Assert.Equal("other", resolved.Sex);
        Assert.Equal(26, resolved.AgeAtRecordingYears);
        var fresh = new SubjectSelectorViewModel();
        await fresh.InitializeAsync(workspace, new(), CancellationToken.None);
        fresh.Selected = fresh.Subjects.Single(item => item.SubjectId == resolved.SubjectId);
        Assert.Equal("other", fresh.Selection.Sex);
        Assert.Null(fresh.Selection.AgeAtRecordingYears);
    }

    [Theory]
    [InlineData("25.5")]
    [InlineData("-1")]
    [InlineData("151")]
    [InlineData("abc")]
    public async Task SharedSelectorRejectsInvalidAgeBeforeResolving(string invalid)
    {
        using var fixture = new Fixture();
        var workspace = await fixture.CreateAsync();
        var selector = new SubjectSelectorViewModel();
        await selector.InitializeAsync(workspace, new(), CancellationToken.None);
        selector.AgeText = invalid;
        Assert.Throws<InvalidOperationException>(() => selector.Selection);
        Assert.Equal(0, fixture.Client.ResolveCount);
    }

    [Fact]
    public async Task LocalRecordAgeSurvivesCentralRefreshRenameAndDeleteAndCanBeEditedIndependently()
    {
        using var fixture = new Fixture();
        var directory = fixture.CreateLocalRecording();
        await RecordingMetadataStore.SaveAsync(directory, "record", new("S001", "原姓名", "person", "female", 25));
        fixture.Client.Subjects[0] = fixture.Client.Subjects[0] with { Sex = "female" };
        var workspace = await fixture.CreateAsync();
        await workspace.RefreshImportedRecordingsAsync();
        await workspace.UpdateSubjectAsync("project", "person", "S002", "更名", CancellationToken.None, "male");
        var row = Assert.Single(workspace.Recordings);
        Assert.Equal(25, row.AgeAtRecordingYears);
        Assert.Equal("male", row.SubjectSex);
        await workspace.EditRecordingAsync(row, "record", new("S002", "更名", "person", "male", 26));
        Assert.Equal(26, Assert.Single(workspace.Recordings).AgeAtRecordingYears);
        await workspace.DeleteSubjectAsync("project", "person", CancellationToken.None);
        var stored = RecordingMetadataStore.ReadSubject(directory);
        Assert.Equal(26, stored.AgeAtRecordingYears);
        Assert.Null(stored.SubjectId);
        Assert.Null(stored.Sex);
    }

    [Fact]
    public async Task ClearingLegacySubjectFieldsKeepsRecordingAgeWithoutCopyingSex()
    {
        using var fixture = new Fixture();
        var directory = fixture.CreateLocalRecording();
        fixture.Client.Subjects[0] = fixture.Client.Subjects[0] with { Sex = "female" };
        await RecordingMetadataStore.SaveAsync(directory, "record", new("S001", "原姓名", "person", "female", 25));
        var workspace = await fixture.CreateAsync();
        await workspace.EditRecordingAsync(Assert.Single(workspace.Recordings), "record", "", "");
        Assert.Equal(new RecordingSubject(AgeAtRecordingYears: 25), RecordingMetadataStore.ReadSubject(directory));
        Assert.Single(fixture.Client.Subjects);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("subject-directory-").FullName;
        public Client Client { get; } = new();
        public async Task<ProjectWorkspaceViewModel> CreateAsync()
        {
            var projectDirectory = Path.Combine(root, "project");
            Directory.CreateDirectory(Path.Combine(projectDirectory, "recordings"));
            var store = new ResearchProjectStore(Path.Combine(root, "projects.json"));
            await store.SaveAsync(new ResearchProject("project", "P001", "项目", "", "", [], "tester",
                projectDirectory, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), CancellationToken.None);
            var workspace = new ProjectWorkspaceViewModel(new OperationNotificationCenter(), store, backendClient: Client);
            await workspace.RefreshAsync();
            return workspace;
        }
        public string CreateLocalRecording()
        {
            var directory = Path.Combine(root, "project", "recordings", "record");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new
            {
                SessionId = Guid.NewGuid(), PayloadFormat = "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
                EegSignalUnit = "V", DeviceId = "device", DeviceName = "device", SamplingRateHz = 500,
                SampleCounterChannelIndex = 1, RecordingStartUtc = DateTimeOffset.UtcNow,
                Channels = new[] { new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
                    new AcquisitionChannel(1, 1, "Counter", AcquisitionChannelKind.SampleCounter, "count") },
                HardwareConfiguration = new Dictionary<string, string>()
            }));
            using var writer = new BinaryWriter(File.Create(Path.Combine(directory, "samples-000001.bin")));
            writer.Write(0L); writer.Write(1); writer.Write(2); writer.Write(DateTimeOffset.UtcNow.UtcTicks);
            writer.Write(1e-6); writer.Write(0d);
            return directory;
        }
        public void Dispose() => Directory.Delete(root, true);
    }

    private sealed class Client : IResearchProjectClient
    {
        public List<BackendResearchSubject> Subjects { get; } = [new("person", "project", "S001", "", "原姓名")];
        public int ResolveCount { get; private set; }
        public bool FailListing { get; set; }
        public Task<BackendResearchProject> EnsureProjectAsync(string p, string n, string d, CancellationToken t) => Task.FromResult(new BackendResearchProject(p, n, d, "", ""));
        public Task<IReadOnlyList<BackendResearchSubject>> ListSubjectsAsync(string p, CancellationToken t) => FailListing
            ? Task.FromException<IReadOnlyList<BackendResearchSubject>>(new IOException("offline"))
            : Task.FromResult<IReadOnlyList<BackendResearchSubject>>(Subjects.ToArray());
        public Task<SubjectResolutionResponse> ResolveSubjectAsync(string p, CreateSubjectRequest request, CancellationToken t)
        {
            ResolveCount++;
            var subject = Subjects.FirstOrDefault(item => !string.IsNullOrEmpty(request.LocalCode) && item.LocalCode == request.LocalCode);
            if (subject is null) { subject = new(Guid.NewGuid().ToString("N"), p, request.LocalCode, "", request.Name, request.Sex); Subjects.Add(subject); }
            return Task.FromResult(new SubjectResolutionResponse(subject));
        }
        public Task<BackendResearchSubject> UpdateSubjectAsync(string p, string id, CreateSubjectRequest request, CancellationToken t)
        {
            var index = Subjects.FindIndex(item => item.SubjectId == id);
            Subjects[index] = Subjects[index] with { LocalCode = request.LocalCode, Name = request.Name, Sex = request.Sex };
            return Task.FromResult(Subjects[index]);
        }
        public Task<SubjectDeletionResponse> DeleteSubjectAsync(string p, string id, CancellationToken t)
        {
            Subjects.RemoveAll(item => item.SubjectId == id);
            return Task.FromResult(new SubjectDeletionResponse(id, 0));
        }
        public Task<IReadOnlyList<BackendResearchSession>> ListSessionsAsync(string p, CancellationToken t) => Task.FromResult<IReadOnlyList<BackendResearchSession>>([]);
        public Task<BackendResearchSubject> CreateSubjectAsync(string p, string c, CancellationToken t) => throw new NotSupportedException();
        public Task<BackendResearchSession> CreateSessionAsync(string p, CreateResearchSessionRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<BackendResearchSession> RenameSessionAsync(string p, string s, string n, CancellationToken t) => throw new NotSupportedException();
        public Task<BackendResearchSession> EditSessionAsync(string p, string s, EditResearchSessionRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<BackendResearchSession> BindSessionSubjectAsync(string p, string s, BindResearchSessionRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<BackendProjectRecordingDeletion> DeleteImportedRecordingAsync(string p, string s, CancellationToken t) => throw new NotSupportedException();
        public Task<BackendImportedRecording> ImportRecordingAsync(string p, string f, string? s, string? c, string? j, IProgress<long>? progress, CancellationToken t, string? newSubjectName = null, string? newSubjectSex = null, int? ageAtRecordingYears = null) => throw new NotSupportedException();
        public Task<BackendRecording> GetRecordingAsync(string r, CancellationToken t) => throw new NotSupportedException();
        public Task<BackendRawSampleRange> ReadRawSamplesAsync(string r, long a, long b, CancellationToken t) => throw new NotSupportedException();
    }
}
