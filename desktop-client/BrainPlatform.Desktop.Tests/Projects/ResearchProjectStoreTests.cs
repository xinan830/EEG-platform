using BrainPlatform.Desktop.Projects;

namespace BrainPlatform.Desktop.Tests.Projects;

public sealed class ResearchProjectStoreTests
{
    [Fact]
    public async Task Store_RoundTripsProjectDirectoryWithoutTouchingProjectFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N"));
        var indexPath = Path.Combine(root, "index", "projects.json");
        var projectDirectory = Path.Combine(root, "research-project");
        Directory.CreateDirectory(projectDirectory);
        var evidencePath = Path.Combine(projectDirectory, "keep.raw");
        await File.WriteAllTextAsync(evidencePath, "immutable");
        var store = new ResearchProjectStore(indexPath);
        var now = DateTimeOffset.UtcNow;
        var project = new ResearchProject(
            "project-id", "P20260919001", "测试项目", "说明", "目的", ["EEG"], "tester",
            projectDirectory, now, now);

        try
        {
            await store.SaveAsync(project, CancellationToken.None);
            var restored = Assert.Single(await store.LoadAsync(CancellationToken.None));
            Assert.Equal(projectDirectory, restored.DirectoryPath);
            Assert.Equal(Path.Combine(projectDirectory, "recordings"), restored.RecordingsDirectory);

            await store.DeleteAsync(project.Id, CancellationToken.None);
            Assert.Empty(await store.LoadAsync(CancellationToken.None));
            Assert.True(File.Exists(evidencePath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AcquisitionRequest_RejectsMissingProjectIdentity()
    {
        var project = new BrainPlatform.Desktop.Acquisition.Contracts.AcquisitionProjectContext(
            "", "", "", Path.GetTempPath(), "{}");
        var request = new BrainPlatform.Desktop.Acquisition.Contracts.AcquisitionStreamRequest("device", 500, project);

        Assert.Throws<ArgumentException>(request.Validate);
    }
}
