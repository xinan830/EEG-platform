using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Projects;

public sealed class ProjectRecordingReviewNavigationTests
{
    [Fact]
    public async Task RefreshRecordingsExposesStableSelectionAndReviewEligibility()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-review-project-tests", Guid.NewGuid().ToString("N"));
        var indexPath = Path.Combine(root, "projects.json");
        var projectDirectory = Path.Combine(root, "project");
        var recordingDirectory = Path.Combine(projectDirectory, "recordings", "recording");
        Directory.CreateDirectory(recordingDirectory);
        var sessionId = Guid.NewGuid();
        WriteManifest(recordingDirectory, sessionId);
        await File.WriteAllTextAsync(Path.Combine(recordingDirectory, "audit.jsonl"), "{\"type\":\"completed\"}\n");
        var project = new ResearchProject(
            "project", "P001", "项目", "", "", [], "tester", projectDirectory,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var store = new ResearchProjectStore(indexPath);
        await store.SaveAsync(project, CancellationToken.None);
        var notifications = new OperationNotificationCenter();
        var viewModel = new ProjectWorkspaceViewModel(notifications, store);

        try
        {
            await viewModel.RefreshAsync();

            var row = Assert.Single(viewModel.Recordings);
            Assert.Equal(sessionId.ToString("N"), row.SessionId);
            Assert.True(row.CanReview);
            viewModel.SelectedRecording = row;
            Assert.Same(row, viewModel.SelectedRecording);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LocalRecordingRenamePersistsWithoutChangingDirectoryOrSessionIdentity()
    {
        var root = Directory.CreateTempSubdirectory("local-recording-rename-").FullName;
        try
        {
            var directory = Path.Combine(root, "project", "recordings", "recording");
            Directory.CreateDirectory(directory);
            WriteManifest(directory, Guid.NewGuid());
            var manifest = await File.ReadAllBytesAsync(Path.Combine(directory, "manifest.json"));
            var store = new ResearchProjectStore(Path.Combine(root, "projects.json"));
            await store.SaveAsync(new ResearchProject("project", "P001", "项目", "", "", [], "tester",
                Path.Combine(root, "project"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), CancellationToken.None);
            var workspace = new ProjectWorkspaceViewModel(new OperationNotificationCenter(), store);
            await workspace.RefreshAsync();
            var original = Assert.Single(workspace.Recordings);

            await workspace.RenameRecordingAsync(original, "  本地复测  ");
            workspace.RefreshRecordings();

            Assert.Equal(original with { Name = "本地复测" }, Assert.Single(workspace.Recordings));
            await workspace.EditRecordingAsync(Assert.Single(workspace.Recordings), "本地复测", "S001", "姓名");
            workspace.RefreshRecordings();
            Assert.Equal("S001 · 姓名", Assert.Single(workspace.Recordings).SubjectText);
            await workspace.RenameRecordingAsync(Assert.Single(workspace.Recordings), "再次命名");
            Assert.Equal("S001 · 姓名", Assert.Single(workspace.Recordings).SubjectText);
            Assert.Equal(manifest, await File.ReadAllBytesAsync(Path.Combine(directory, "manifest.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void WriteManifest(string directory, Guid sessionId)
    {
        var manifest = new
        {
            SessionId = sessionId,
            PayloadFormat = "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
            EegSignalUnit = "V",
            DeviceId = "device",
            DeviceName = "device",
            SamplingRateHz = 500,
            SampleCounterChannelIndex = 1,
            RecordingStartUtc = DateTimeOffset.UtcNow,
            Channels = new[]
            {
                new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(1, 1, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
            },
            Project = new AcquisitionProjectContext("project", "P001", "项目", directory, "{}"),
            HardwareConfiguration = new Dictionary<string, string>(),
        };
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest));
        using var stream = File.Create(Path.Combine(directory, "samples-000001.bin"));
        using var writer = new BinaryWriter(stream);
        writer.Write(0L);
        writer.Write(1);
        writer.Write(2);
        writer.Write(DateTimeOffset.UtcNow.UtcTicks);
        writer.Write(1e-6);
        writer.Write(0L);
    }
}
