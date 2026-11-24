using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Projects;

public sealed class ProjectDirectoryCreationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "brain-project-directory-tests", Guid.NewGuid().ToString("N"));
    private readonly ResearchProjectStore store;
    private readonly ProjectWorkspaceViewModel workspace;
    private readonly ProjectDirectoryPolicy directories;

    public ProjectDirectoryCreationTests()
    {
        store = new ResearchProjectStore(Path.Combine(root, "projects.json"));
        directories = new ProjectDirectoryPolicy(Path.Combine(root, "application"));
        workspace = new ProjectWorkspaceViewModel(new OperationNotificationCenter(), store, directories: directories);
    }

    [Fact]
    public async Task CustomDirectoryIsUsedExactlyAndExistingFilesArePreserved()
    {
        var directory = Path.Combine(root, "chosen");
        Directory.CreateDirectory(directory);
        var evidence = Path.Combine(directory, "keep.txt");
        await File.WriteAllTextAsync(evidence, "keep");

        var project = await workspace.SaveAsync(Draft("实验一", directory));

        Assert.Equal(directory, project.DirectoryPath);
        Assert.True(Directory.Exists(project.RecordingsDirectory));
        Assert.Equal("keep", await File.ReadAllTextAsync(evidence));
        Assert.Equal(directory, Assert.Single(await store.LoadAsync(CancellationToken.None)).DirectoryPath);
    }

    [Fact]
    public async Task RenamingExistingProjectKeepsItsDirectoryAndIdentity()
    {
        var original = await workspace.SaveAsync(Draft("原名", Path.Combine(root, "original")));

        var renamed = await workspace.SaveAsync(Draft("新名", original.DirectoryPath), original);

        Assert.Equal("新名", renamed.Name);
        Assert.Equal(original.Id, renamed.Id);
        Assert.Equal(original.DirectoryPath, renamed.DirectoryPath);
        Assert.Equal(original.RecordingsDirectory, renamed.RecordingsDirectory);
    }

    [Fact]
    public async Task PopulatedProjectCannotBeReassignedToAnotherDirectory()
    {
        var original = await workspace.SaveAsync(Draft("实验一", Path.Combine(root, "original")));
        var recording = Path.Combine(original.RecordingsDirectory, "session");
        Directory.CreateDirectory(recording);
        var manifest = Path.Combine(recording, "manifest.json");
        var contents = JsonSerializer.Serialize(new
        {
            SessionId = Guid.NewGuid(), RecordingStartUtc = DateTimeOffset.UtcNow,
            DeviceName = "test", SamplingRateHz = 500, Channels = Array.Empty<object>(),
        });
        await File.WriteAllTextAsync(manifest, contents);
        var destination = Path.Combine(root, "different");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workspace.SaveAsync(Draft("实验一", destination), original));

        Assert.Contains("已有采集记录", error.Message);
        Assert.False(Directory.Exists(destination));
        Assert.Equal(contents, await File.ReadAllTextAsync(manifest));
        Assert.Equal(original.DirectoryPath, Assert.Single(await store.LoadAsync(CancellationToken.None)).DirectoryPath);
    }

    [Fact]
    public async Task NameOnlyCreatesProjectAndRecordingsBesideTheApplication()
    {
        var project = await workspace.SaveAsync(Draft(" 实验一 "));

        Assert.Equal("实验一", project.Name);
        Assert.Equal(Path.Combine(root, "application", "data", "实验一"), project.DirectoryPath);
        Assert.True(Directory.Exists(project.RecordingsDirectory));
        Assert.Equal(project.DirectoryPath, Assert.Single(await store.LoadAsync(CancellationToken.None)).DirectoryPath);
    }

    [Fact]
    public void DefaultRootUsesTheExecutableDirectory() =>
        Assert.Equal(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data")), new ProjectDirectoryPolicy().DefaultRoot);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("D:\\outside")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("a?b")]
    [InlineData("name.")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("NUL")]
    [InlineData("AUX")]
    [InlineData("PRN")]
    [InlineData("COM1")]
    [InlineData("LPT9.dat")]
    [InlineData("COM¹")]
    [InlineData("CONIN$")]
    public async Task InvalidDefaultNamesDoNotCreateDirectoriesOrIndexEntries(string name)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.SaveAsync(Draft(name)));

        Assert.False(Directory.Exists(root));
        Assert.Empty(await store.LoadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OccupiedDefaultFileOrDirectoryIsNotReused(bool isFile)
    {
        Directory.CreateDirectory(directories.DefaultRoot);
        var destination = directories.GetDefaultDirectory("实验一");
        var evidence = isFile ? destination : Path.Combine(destination, "keep.raw");
        if (!isFile) Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(evidence, "immutable");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.SaveAsync(Draft("实验一")));

        Assert.Contains("已存在", error.Message);
        Assert.Equal("immutable", await File.ReadAllTextAsync(evidence));
        Assert.False(Directory.Exists(Path.Combine(destination, "recordings")));
        Assert.Empty(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DirectoryCreationFailureNamesDestinationAndDoesNotSaveProject()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(directories.DefaultRoot)!);
        await File.WriteAllTextAsync(directories.DefaultRoot, "blocked");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.SaveAsync(Draft("实验一")));

        Assert.Contains(directories.GetDefaultDirectory("实验一"), error.Message);
        Assert.IsAssignableFrom<IOException>(error.InnerException);
        Assert.Equal("blocked", await File.ReadAllTextAsync(directories.DefaultRoot));
        Assert.Empty(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RenameWithNoDirectoryKeepsExistingLocation()
    {
        var original = await workspace.SaveAsync(Draft("实验一"));
        var renamed = await workspace.SaveAsync(Draft("实验二"), original);

        Assert.Equal(original.DirectoryPath, renamed.DirectoryPath);
        Assert.False(Directory.Exists(directories.GetDefaultDirectory("实验二")));
    }

    [Fact]
    public async Task RemovingProjectKeepsDataAndPreventsAutomaticReuse()
    {
        var project = await workspace.SaveAsync(Draft("实验一"));
        var evidence = Path.Combine(project.DirectoryPath, "keep.raw");
        await File.WriteAllTextAsync(evidence, "immutable");
        await workspace.DeleteSelectedAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.SaveAsync(Draft("实验一")));

        Assert.Equal("immutable", await File.ReadAllTextAsync(evidence));
        Assert.Empty(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentDefaultDirectoryCreationDoesNotMergeDestinations()
    {
        var destination = directories.GetDefaultDirectory("实验一");
        using var start = new ManualResetEventSlim(false);
        var attempts = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            start.Wait();
            return Record.Exception(() => directories.CreateProjectDirectory(destination, isDefault: true));
        })).ToArray();

        start.Set();
        var errors = await Task.WhenAll(attempts);

        Assert.Single(errors, error => error is null);
        var conflict = Assert.IsType<InvalidOperationException>(Assert.Single(errors, error => error is not null));
        Assert.Contains("已存在", conflict.Message);
        Assert.True(Directory.Exists(Path.Combine(destination, "recordings")));
        Assert.Equal(destination, Assert.Single(Directory.GetDirectories(directories.DefaultRoot)));
    }

    private static ResearchProjectDraft Draft(string name, string directory = "") =>
        new(name, "", "", "", "tester", directory);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
