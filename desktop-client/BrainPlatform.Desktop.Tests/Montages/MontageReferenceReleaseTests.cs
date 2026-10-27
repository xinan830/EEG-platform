using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Montages;

public sealed class MontageReferenceReleaseTests
{
    private static readonly CancellationToken Token = CancellationToken.None;

    [Fact]
    public async Task LastPermanentDeletionReleasesArchivedPresetButNotHistoricalEditLock()
    {
        var setup = await SetupAsync();
        var directory = await WriteRecordingAsync(setup.Root, "recordings/one", setup.Profile);
        await setup.Store.LoadAsync(Token);
        await setup.Store.ArchiveAsync(setup.Profile.Id, Token);
        Directory.Delete(directory, true);
        var restarted = new MontageProfileStore(setup.Path, setup.Projects);
        var loaded = Assert.Single(await restarted.LoadAsync(Token));
        Assert.True(loaded.IsArchived);
        Assert.True(loaded.IsRecordingReferenced);
        Assert.False(loaded.HasRecordingReferences);
        Assert.False(loaded.CanEdit);
        Assert.True(loaded.CanDelete);
        await restarted.DeleteAsync(loaded.Id, Token);
        Assert.Empty(await restarted.LoadAsync(Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.SaveAsync(setup.Profile, Token));
    }

    [Fact]
    public async Task SoftDeletionAndRestorationPreserveProtectionAndHistoricalSnapshot()
    {
        var setup = await SetupAsync();
        var directory = await WriteRecordingAsync(setup.Root, "recordings/one", setup.Profile);
        await setup.Store.LoadAsync(Token);
        var original = await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json"));
        var trash = Path.Combine(setup.Root, ".trash", "recordings", "one");
        Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
        Directory.Move(directory, trash);
        await AssertProtectedAsync(setup.Store, setup.Profile.Id);
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(trash, "manifest.json")));
        Directory.Move(trash, directory);
        await AssertProtectedAsync(new MontageProfileStore(setup.Path, setup.Projects), setup.Profile.Id);
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json")));
    }

    [Fact]
    public async Task RemovingOneOfMultipleReferencesCannotReleaseThePreset()
    {
        var setup = await SetupAsync();
        var first = await WriteRecordingAsync(setup.Root, "recordings/one", setup.Profile);
        var last = await WriteRecordingAsync(setup.Root, ".trash/recordings/two", setup.Profile);
        await setup.Store.LoadAsync(Token);
        Directory.Delete(first, true);
        await AssertProtectedAsync(setup.Store, setup.Profile.Id);
        Directory.Delete(last, true);
        Assert.True(Assert.Single(await setup.Store.LoadAsync(Token)).CanDelete);
        await setup.Store.DeleteAsync(setup.Profile.Id, Token);
    }

    [Fact]
    public async Task OrphanedLegacyIdArrayMigratesWithoutClearingHistoricalIdentity()
    {
        var setup = await SetupAsync();
        await File.WriteAllTextAsync($"{setup.Path}.references.json", JsonSerializer.Serialize(new[] { setup.Profile.Id }));
        var loaded = Assert.Single(await setup.Store.LoadAsync(Token));
        Assert.True(loaded.CanDelete);
        Assert.False(loaded.CanEdit);
        Assert.True(loaded.IsRecordingReferenced);
        using var index = JsonDocument.Parse(await File.ReadAllTextAsync($"{setup.Path}.references.json"));
        Assert.Equal(2, index.RootElement.GetProperty("Version").GetInt32());
        Assert.Equal(setup.Profile.Id, Assert.Single(index.RootElement.GetProperty("HistoricalIds").EnumerateArray()).GetString());
        await setup.Store.DeleteAsync(setup.Profile.Id, Token);
    }

    [Theory]
    [InlineData("recordings/one")]
    [InlineData(".trash/recordings/one")]
    public async Task LegacyIdArrayStillProtectsExistingRecordings(string relative)
    {
        var setup = await SetupAsync();
        await File.WriteAllTextAsync($"{setup.Path}.references.json", JsonSerializer.Serialize(new[] { setup.Profile.Id }));
        await WriteRecordingAsync(setup.Root, relative, setup.Profile);
        await AssertProtectedAsync(setup.Store, setup.Profile.Id);
    }

    [Fact]
    public async Task RemovingProjectFromCatalogDoesNotReleaseFilesOrItsTrash()
    {
        var setup = await SetupAsync();
        await WriteRecordingAsync(setup.Root, ".trash/recordings/one", setup.Profile);
        await setup.Store.LoadAsync(Token);
        await setup.Projects.DeleteAsync("p", Token);
        await AssertProtectedAsync(new MontageProfileStore(setup.Path, setup.Projects), setup.Profile.Id);
    }

    [Fact]
    public async Task RegisteredExternalRecordingRemainsDiscoverableAfterRestart()
    {
        var setup = await SetupAsync();
        var external = await WriteRecordingAsync(NewRoot(), "external", setup.Profile);
        await setup.Store.RegisterRecordingReferenceAsync(Metadata(setup.Profile), Token, recordingDirectory: external);
        var restarted = new MontageProfileStore(setup.Path, setup.Projects);
        await AssertProtectedAsync(restarted, setup.Profile.Id);
        Directory.Delete(external, true);
        Assert.True(Assert.Single(await restarted.LoadAsync(Token)).CanDelete);
    }

    [Fact]
    public async Task CorruptRecordingCannotEraseReferenceState()
    {
        var setup = await SetupAsync();
        var directory = await WriteRecordingAsync(setup.Root, "recordings/one", setup.Profile);
        await setup.Store.LoadAsync(Token);
        var originalIndex = await File.ReadAllTextAsync($"{setup.Path}.references.json");
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), "{broken");
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.DeleteAsync(setup.Profile.Id, Token));
        Assert.Equal(originalIndex, await File.ReadAllTextAsync($"{setup.Path}.references.json"));
    }

    [Fact]
    public async Task MissingManifestWithRemainingRawFilesIsNotPermanentDeletion()
    {
        var setup = await SetupAsync();
        var directory = await WriteRecordingAsync(setup.Root, "recordings/one", setup.Profile);
        await setup.Store.LoadAsync(Token);
        await File.WriteAllTextAsync(Path.Combine(directory, "samples-000001.bin"), "fixture");
        File.Delete(Path.Combine(directory, "manifest.json"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.DeleteAsync(setup.Profile.Id, Token));
        Assert.Contains("无法验证", error.Message);
    }

    [Fact]
    public async Task LegacyRawFilesWithoutManifestCannotBeMissedDuringMigration()
    {
        var setup = await SetupAsync();
        await File.WriteAllTextAsync($"{setup.Path}.references.json", JsonSerializer.Serialize(new[] { setup.Profile.Id }));
        var directory = Path.Combine(setup.Root, ".trash", "recordings", "incomplete");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "samples-000001.bin"), "fixture");
        var originalIndex = await File.ReadAllTextAsync($"{setup.Path}.references.json");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.DeleteAsync(setup.Profile.Id, Token));
        Assert.Contains("无法验证", error.Message);
        Assert.Equal(originalIndex, await File.ReadAllTextAsync($"{setup.Path}.references.json"));
    }

    [Fact]
    public async Task WorkspaceRefreshEnablesDeletionAfterLastReferenceIsRemoved()
    {
        var setup = await SetupAsync();
        var directory = await WriteRecordingAsync(setup.Root, "recordings/one", setup.Profile);
        var channelStore = new ChannelConfigurationProfileStore(Path.Combine(setup.Root, "channels.json"));
        await channelStore.SaveAsync(setup.Profile.ChannelConfigurationSnapshot, Token);
        var channels = new ChannelConfigurationWorkspaceViewModel(
            new ChannelMappingViewModel(new ChannelLabelMappingStore(Path.Combine(setup.Root, "labels.json"))),
            channelStore, montageStore: setup.Store);
        var workspace = new MontageConfigurationWorkspaceViewModel(channels, setup.Store);
        await workspace.RefreshAsync();
        workspace.SelectedProfile = workspace.Profiles.Single(profile => profile.Id == setup.Profile.Id);
        Assert.False(workspace.CanDeleteSelected);
        Directory.Delete(directory, true);
        await workspace.RefreshReferencesAsync();
        Assert.True(workspace.CanDeleteSelected);
        Assert.False(workspace.SelectedProfile!.CanEdit);
        await workspace.DeleteSelectedAsync();
        Assert.DoesNotContain(workspace.Profiles, profile => profile.Id == setup.Profile.Id);
    }

    [Fact]
    public async Task UnavailableVolumeCannotBeMistakenForDeletedRecording()
    {
        var setup = await SetupAsync();
        var unusedDrive = Enumerable.Range('D', 'Z' - 'D' + 1).Select(value => $"{(char)value}:\\")
            .First(root => !Directory.Exists(root));
        await File.WriteAllTextAsync($"{setup.Path}.references.json", JsonSerializer.Serialize(new
        {
            Version = 2,
            HistoricalIds = new[] { setup.Profile.Id },
            UnlocatedIds = Array.Empty<string>(),
            Roots = new[] { Path.Combine(unusedDrive, "recordings") },
            Recordings = new Dictionary<string, string[]>(),
        }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.DeleteAsync(setup.Profile.Id, Token));
        Assert.Contains("无法验证", error.Message);
    }

    [Fact]
    public async Task DeleteWaitsForPublicationAndCannotPassTheMissingManifestGap()
    {
        var setup = await SetupAsync();
        var directory = Path.Combine(setup.Root, "recordings", "one");
        var publishing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var creation = setup.Store.PublishRecordingAsync(Metadata(setup.Profile), directory,
            [Path.Combine(setup.Root, "recordings")], async () =>
            {
                publishing.SetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await WriteRecordingAsync(setup.Root, "recordings/one", setup.Profile);
                return 1;
            }, Token);
        await publishing.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var deletion = new MontageProfileStore(setup.Path, setup.Projects).DeleteAsync(setup.Profile.Id, Token);
        try { Assert.False(deletion.IsCompleted); }
        finally { release.TrySetResult(); }
        Assert.Equal(1, await creation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => deletion);
        await AssertProtectedAsync(setup.Store, setup.Profile.Id);
    }

    [Fact]
    public async Task FailedPublicationWithoutFilesDoesNotPermanentlyProhibitDeletion()
    {
        var setup = await SetupAsync();
        await Assert.ThrowsAsync<IOException>(() => setup.Store.PublishRecordingAsync<int>(Metadata(setup.Profile),
            Path.Combine(setup.Root, "recordings", "failed"), [Path.Combine(setup.Root, "recordings")],
            () => throw new IOException("Fixture publication failure"), Token));
        var loaded = Assert.Single(await setup.Store.LoadAsync(Token));
        Assert.True(loaded.CanDelete);
        Assert.False(loaded.CanEdit);
        await setup.Store.DeleteAsync(setup.Profile.Id, Token);
    }

    [Fact]
    public async Task UnlocatedRegistrationStaysProtectedRatherThanAssumingDeletion()
    {
        var setup = await SetupAsync();
        await setup.Store.RegisterRecordingReferenceAsync(Metadata(setup.Profile), Token);
        await AssertProtectedAsync(new MontageProfileStore(setup.Path, setup.Projects), setup.Profile.Id);
    }

    [Fact]
    public async Task TrailingSeparatorCannotRegisterAConflictingIdentityForTheSameDirectory()
    {
        var setup = await SetupAsync();
        var directory = await WriteRecordingAsync(NewRoot(), "external", setup.Profile);
        await setup.Store.RegisterRecordingReferenceAsync(Metadata(setup.Profile), Token,
            recordingDirectory: directory + Path.DirectorySeparatorChar);
        await setup.Store.RegisterRecordingReferenceAsync(Metadata(setup.Profile), Token, recordingDirectory: directory);
        var originalIndex = await File.ReadAllTextAsync($"{setup.Path}.references.json");
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.RegisterRecordingReferenceAsync(
            Metadata(setup.Profile with { Id = "conflicting-id" }), Token, recordingDirectory: directory));
        Assert.Equal(originalIndex, await File.ReadAllTextAsync($"{setup.Path}.references.json"));
        await AssertProtectedAsync(setup.Store, setup.Profile.Id);
    }

    [Fact]
    public async Task ChangedManifestIdentityCannotReleaseOriginalReference()
    {
        var setup = await SetupAsync();
        await WriteRecordingAsync(setup.Root, "recordings/one", setup.Profile);
        await setup.Store.LoadAsync(Token);
        var originalIndex = await File.ReadAllTextAsync($"{setup.Path}.references.json");
        await WriteRecordingAsync(setup.Root, "recordings/one", setup.Profile with { Id = "changed-id" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.DeleteAsync(setup.Profile.Id, Token));
        Assert.Equal(originalIndex, await File.ReadAllTextAsync($"{setup.Path}.references.json"));
    }

    [Fact]
    public async Task EmptyDiscoveredIdentityCanBeReconciledFromRecordingManifest()
    {
        var setup = await SetupAsync();
        var directory = Path.Combine(setup.Root, "recordings", "one");
        await WriteRecordingAsync(setup.Root, "recordings/one", setup.Profile);
        var emptyIndex = new
        {
            Version = 2,
            HistoricalIds = Array.Empty<string>(),
            UnlocatedIds = Array.Empty<string>(),
            Roots = new[] { Path.Combine(setup.Root, "recordings") },
            Recordings = new Dictionary<string, string[]> { [directory] = [] },
        };
        await File.WriteAllTextAsync($"{setup.Path}.references.json", JsonSerializer.Serialize(emptyIndex));

        await AssertProtectedAsync(new MontageProfileStore(setup.Path, setup.Projects), setup.Profile.Id);

        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync($"{setup.Path}.references.json"));
        Assert.Equal(setup.Profile.Id, Assert.Single(persisted.RootElement.GetProperty("Recordings")
            .GetProperty(directory).EnumerateArray()).GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Version\":2,\"HistoricalIds\":[],\"UnlocatedIds\":[],\"Roots\":[],\"Recordings\":{\"C:/fixture\":[\"unknown-id\"]}}")]
    [InlineData("{broken")]
    public async Task InvalidIndexDoesNotSilentlyReleaseDeletion(string json)
    {
        var setup = await SetupAsync();
        await File.WriteAllTextAsync($"{setup.Path}.references.json", json);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.DeleteAsync(setup.Profile.Id, Token));
        Assert.Contains("无法验证", error.Message);
        Assert.Equal(json, await File.ReadAllTextAsync($"{setup.Path}.references.json"));
        Assert.True(File.Exists(setup.Path));
    }

    [Fact]
    public async Task CancelledRefreshDoesNotPublishPartialIndex()
    {
        var setup = await SetupAsync();
        await WriteRecordingAsync(setup.Root, "recordings/one", setup.Profile);
        await setup.Store.LoadAsync(Token);
        var originalIndex = await File.ReadAllTextAsync($"{setup.Path}.references.json");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.Store.LoadAsync(cancelled.Token));
        Assert.Equal(originalIndex, await File.ReadAllTextAsync($"{setup.Path}.references.json"));
        await AssertProtectedAsync(setup.Store, setup.Profile.Id);
    }

    private static async Task AssertProtectedAsync(MontageProfileStore store, string id)
    {
        var profile = Assert.Single(await store.LoadAsync(Token));
        Assert.True(profile.HasRecordingReferences);
        Assert.False(profile.CanDelete);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync(id, Token));
    }

    private static async Task<Setup> SetupAsync()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);
        var projects = new ResearchProjectStore(Path.Combine(root, "projects.json"));
        await projects.SaveAsync(new ResearchProject("p", "p", "p", "", "", [], "", root,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), Token);
        var path = Path.Combine(root, "montages.json");
        var store = new MontageProfileStore(path, projects);
        var profile = MontageAppearanceTests.Profile();
        await store.SaveAsync(profile, Token);
        return new(root, path, projects, store, profile);
    }

    private static async Task<string> WriteRecordingAsync(string root, string relative, MontageProfile profile)
    {
        var directory = Path.Combine(root, relative);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(new { HardwareConfiguration = Metadata(profile) }));
        return directory;
    }

    private static Dictionary<string, string> Metadata(MontageProfile profile) => new()
    {
        ["montage_configuration_id"] = profile.Id,
        ["montage_configuration_snapshot_json"] = JsonSerializer.Serialize(profile),
    };

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N"));
    private sealed record Setup(string Root, string Path, ResearchProjectStore Projects, MontageProfileStore Store, MontageProfile Profile);
}
