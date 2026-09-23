using BrainPlatform.Desktop.ViewModels;
using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Projects;

public sealed class ProjectWorkspaceFilteringTests
{
    [Fact]
    public async Task FiltersProjectsBySearchStatusAndInclusiveCreatedDateRange()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-project-filter-tests", Guid.NewGuid().ToString("N"));
        var indexPath = Path.Combine(root, "projects.json");
        var store = new ResearchProjectStore(indexPath);
        var activeDirectory = Path.Combine(root, "active");
        var finishedDirectory = Path.Combine(root, "finished");
        Directory.CreateDirectory(activeDirectory);
        Directory.CreateDirectory(finishedDirectory);

        try
        {
            await store.SaveAsync(CreateProject("active", "睡眠研究", activeDirectory,
                new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero), "进行中"), CancellationToken.None);
            await store.SaveAsync(CreateProject("finished", "癫痫研究", finishedDirectory,
                new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero), "已完成"), CancellationToken.None);
            var workspace = new ProjectWorkspaceViewModel(new OperationNotificationCenter(), store);

            await workspace.RefreshAsync();
            workspace.SearchText = "研究";
            workspace.ProjectStatusFilter = "已完成";
            workspace.CreatedFrom = new DateTime(2026, 9, 15);
            workspace.CreatedTo = new DateTime(2026, 9, 15);

            var result = Assert.Single(workspace.FilteredProjects);
            Assert.Equal("癫痫研究", result.Name);
            Assert.Equal("已完成", result.Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreatedTimePresetFiltersWithoutRequiringDateTextInput()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-project-time-preset-tests", Guid.NewGuid().ToString("N"));
        var indexPath = Path.Combine(root, "projects.json");
        var store = new ResearchProjectStore(indexPath);
        var today = new DateTimeOffset(DateTime.Today, TimeZoneInfo.Local.GetUtcOffset(DateTime.Today));

        try
        {
            await store.SaveAsync(CreateProject("today", "今日项目", Path.Combine(root, "today"), today, "进行中"), CancellationToken.None);
            await store.SaveAsync(CreateProject("older", "历史项目", Path.Combine(root, "older"), today.AddDays(-8), "进行中"), CancellationToken.None);
            var workspace = new ProjectWorkspaceViewModel(new OperationNotificationCenter(), store);

            await workspace.RefreshAsync();
            workspace.CreatedTimeFilter = "近7天";

            var result = Assert.Single(workspace.FilteredProjects);
            Assert.Equal("今日项目", result.Name);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshProjectsCountsRecordingsInsteadOfProjectTags()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-project-count-tests", Guid.NewGuid().ToString("N"));
        var indexPath = Path.Combine(root, "projects.json");
        var directory = Path.Combine(root, "project");
        WriteRecordingManifest(Path.Combine(directory, "recordings", "one"));
        WriteRecordingManifest(Path.Combine(directory, "recordings", "two"));
        var store = new ResearchProjectStore(indexPath);

        try
        {
            await store.SaveAsync(CreateProject("project", "项目", directory,
                new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero), "进行中", ["标签1", "标签2", "标签3"]), CancellationToken.None);
            var workspace = new ProjectWorkspaceViewModel(new OperationNotificationCenter(), store);

            await workspace.RefreshAsync();

            var project = Assert.Single(workspace.Projects);
            Assert.Equal(2, project.RecordingCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RecordingPagerShowsOnlyTheSelectedPageWithoutDroppingTheFullProjectRecordCount()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-recording-page-tests", Guid.NewGuid().ToString("N"));
        var indexPath = Path.Combine(root, "projects.json");
        var directory = Path.Combine(root, "project");
        foreach (var index in Enumerable.Range(1, 11))
        {
            WriteRecordingManifest(Path.Combine(directory, "recordings", index.ToString()));
        }
        var store = new ResearchProjectStore(indexPath);

        try
        {
            await store.SaveAsync(CreateProject("project", "项目", directory,
                new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero), "进行中"), CancellationToken.None);
            var workspace = new ProjectWorkspaceViewModel(new OperationNotificationCenter(), store);

            await workspace.RefreshAsync();
            workspace.NextRecordingPage();

            Assert.Equal(11, workspace.Recordings.Count);
            Assert.Equal(2, workspace.RecordingTotalPages);
            Assert.Equal(2, workspace.RecordingCurrentPage);
            Assert.Single(workspace.PagedRecordings);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ResearchProject CreateProject(
        string id,
        string name,
        string directory,
        DateTimeOffset createdAt,
        string status,
        IReadOnlyList<string>? tags = null) =>
        new(id, $"P{id}", name, "说明", "目的", tags ?? [], "tester", directory, createdAt, createdAt, status);

    private static void WriteRecordingManifest(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new
        {
            SessionId = Guid.NewGuid(),
            RecordingStartUtc = DateTimeOffset.UtcNow,
            DeviceName = "设备",
            SamplingRateHz = 500,
            Channels = Array.Empty<object>(),
        }));
    }
}
