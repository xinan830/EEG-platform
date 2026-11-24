using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Montages;

public sealed class MontageLifecycleTests
{
    private static readonly CancellationToken Token = CancellationToken.None;

    [Fact]
    public async Task UnusedProfileRemainsEditableAndDeletable()
    {
        var store = Store();
        var original = Profile();
        await store.SaveAsync(original, Token);
        await store.SaveAsync(original with { Name = "未引用编辑", Revision = 2 }, Token);
        var loaded = Assert.Single(await store.LoadAsync(Token));
        Assert.True(loaded.CanEdit);
        Assert.Equal("未引用编辑", loaded.Name);
        await store.DeleteAsync(original.Id, Token);
        Assert.Empty(await store.LoadAsync(Token));
    }

    [Fact]
    public async Task ReferencedIdRejectsAllEditsAndDeletionAfterRestart()
    {
        var path = Path.Combine(Root(), "montages.json");
        var store = new MontageProfileStore(path);
        var original = Profile();
        await store.SaveAsync(original, Token);
        await store.RegisterRecordingReferenceAsync(Metadata(original), Token, validateCurrentPreset: true);
        store = new MontageProfileStore(path);
        var loaded = Assert.Single(await store.LoadAsync(Token));
        Assert.False(loaded.CanEdit);
        Assert.False(loaded.CanDelete);
        Assert.True(loaded.CanCopy);
        Assert.Equal("被引用", (loaded with { ChannelSnapshotStatusLabel = "已同步" }).ListStatusLabel);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(original with { Name = "覆盖" }, Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(original with
        {
            DerivedChannels = original.DerivedChannels.Select(row => row with { Color = "#123456" }).ToArray(),
        }, Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync(original.Id, Token));
        Assert.Equal(original.Name, Assert.Single(await store.LoadAsync(Token)).Name);
    }

    [Fact]
    public async Task ReadOnlyEditorCanCopyToNewIdWithoutChangingHistory()
    {
        var store = Store();
        var original = Profile();
        await store.SaveAsync(original, Token);
        await store.RegisterRecordingReferenceAsync(Metadata(original), Token);
        var channels = new ChannelConfigurationWorkspaceViewModel(new ChannelMappingViewModel(),
            new ChannelConfigurationProfileStore(Path.Combine(Root(), "channels.json")), montageStore: store);
        var workspace = new MontageConfigurationWorkspaceViewModel(channels, store)
        {
            SelectedProfile = Assert.Single(await store.LoadAsync(Token)),
        };
        workspace.BeginEditSelected();
        Assert.False(workspace.CanEditDraft);
        Assert.Contains("历史记录", workspace.DraftPermissionText);
        Assert.False(workspace.OutputOrder.MoveDownCommand.CanExecute(workspace.DraftRows[0]));
        await Assert.ThrowsAsync<InvalidOperationException>(workspace.SaveDraftAsync);
        workspace.BeginCopySelected();
        Assert.True(workspace.CanEditDraft);
        workspace.DraftName = "新版本";
        workspace.DraftRows[0].Color = "#123456";
        await workspace.SaveDraftAsync();
        var profiles = await store.LoadAsync(Token);
        var copy = profiles.Single(profile => profile.Id != original.Id);
        Assert.Equal(1, copy.Revision);
        Assert.True(copy.CanEdit);
        Assert.Equal(original.ChannelConfigurationSnapshot.Id, copy.ChannelConfigurationSnapshot.Id);
        Assert.Equal("#123456", copy.DerivedChannels[0].Color);
        Assert.Null(profiles.Single(profile => profile.Id == original.Id).DerivedChannels[0].Color);
    }

    [Theory]
    [InlineData("recordings")]
    [InlineData(".trash/recordings")]
    public async Task LegacyProjectManifestsCreateDurableLocks(string relativeDirectory)
    {
        var root = Root();
        var projects = new ResearchProjectStore(Path.Combine(root, "projects.json"));
        await projects.SaveAsync(new ResearchProject("p", "p", "p", "", "", [], "", root,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), Token);
        var store = new MontageProfileStore(Path.Combine(root, "montages.json"), projects);
        var original = Profile();
        await store.SaveAsync(original, Token);
        var directory = Path.Combine(root, relativeDirectory, "recording");
        Directory.CreateDirectory(directory);
        var manifest = Path.Combine(directory, "manifest.json");
        var json = JsonSerializer.Serialize(new { HardwareConfiguration = Metadata(original) });
        await File.WriteAllTextAsync(manifest, json);
        Assert.False(Assert.Single(await store.LoadAsync(Token)).CanEdit);
        Assert.Equal(json, await File.ReadAllTextAsync(manifest));
        File.Delete(manifest);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(original, Token));
    }

    [Fact]
    public async Task CorruptHistoricalMetadataDoesNotSilentlyUnlockProfiles()
    {
        var root = Root();
        var projects = new ResearchProjectStore(Path.Combine(root, "projects.json"));
        await projects.SaveAsync(new ResearchProject("p", "p", "p", "", "", [], "", root,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), Token);
        var store = new MontageProfileStore(Path.Combine(root, "montages.json"), projects);
        var original = Profile();
        await store.SaveAsync(original, Token);
        var directory = Path.Combine(root, "recordings", "broken");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), "{broken");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(original, Token));
        Assert.Contains("历史导联引用", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync(original.Id, Token));
    }

    [Fact]
    public async Task StalePreviewSnapshotIsRejectedBeforeLocking()
    {
        var store = Store();
        var original = Profile();
        await store.SaveAsync(original with { Name = "更新的预设" }, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RegisterRecordingReferenceAsync(Metadata(original), Token, validateCurrentPreset: true));
        Assert.True(Assert.Single(await store.LoadAsync(Token)).CanEdit);
    }

    [Fact]
    public async Task DeletedPreviewPresetCannotStartRecordingOrAcquireLock()
    {
        var store = Store();
        var original = Profile();
        await store.SaveAsync(original, Token);
        await store.DeleteAsync(original.Id, Token);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RegisterRecordingReferenceAsync(Metadata(original), Token, validateCurrentPreset: true));
        Assert.Contains("已删除", error.Message);
        Assert.Empty(await store.GetReferencedIdsAsync(Token));
    }

    [Fact]
    public async Task ConflictingSnapshotIdentityCannotLockEitherProfile()
    {
        var store = Store();
        var original = Profile();
        var metadata = Metadata(original);
        metadata["montage_configuration_id"] = "another-profile";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RegisterRecordingReferenceAsync(metadata, Token, validateCurrentPreset: true));
        Assert.Empty(await store.GetReferencedIdsAsync(Token));
    }

    [Fact]
    public async Task PresentBlankSnapshotIsRejectedDuringStrictAcquisitionValidation()
    {
        var store = Store();
        var original = Profile();
        await store.SaveAsync(original, Token);
        var metadata = new Dictionary<string, string>
        {
            ["montage_configuration_id"] = original.Id,
            ["montage_configuration_snapshot_json"] = "  ",
        };

        await Assert.ThrowsAsync<JsonException>(() =>
            store.RegisterRecordingReferenceAsync(metadata, Token, validateCurrentPreset: true));
        Assert.Empty(await store.GetReferencedIdsAsync(Token));
    }

    [Fact]
    public async Task ArchiveKeepsReferencedSnapshotFieldsAndAllowsCopy()
    {
        var store = Store();
        var original = Profile();
        await store.SaveAsync(original, Token);
        await store.RegisterRecordingReferenceAsync(Metadata(original), Token);
        await store.ArchiveAsync(original.Id, Token);
        var archived = Assert.Single(await store.LoadAsync(Token));
        Assert.True(archived.IsArchived);
        Assert.False(archived.CanEdit);
        Assert.False(archived.CanDelete);
        Assert.True(archived.CanCopy);
        Assert.Equal(original.Fingerprint, archived.Fingerprint);
        Assert.Equal(JsonSerializer.Serialize(original.DerivedChannels), JsonSerializer.Serialize(archived.DerivedChannels));
        Assert.False(archived.CanArchive);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RegisterRecordingReferenceAsync(Metadata(original), Token, validateCurrentPreset: true));
    }

    [Fact]
    public async Task RawWriterPublishesLockBeforeManifestAndPreservesSnapshot()
    {
        var root = Root();
        var store = new MontageProfileStore(Path.Combine(root, "montages.json"));
        var profile = Profile();
        await store.SaveAsync(profile, Token);
        var metadata = new AcquisitionStreamMetadata("device", "test", 500,
            [new(0, 3, "F3", AcquisitionChannelKind.Reference, "V"),
                new(1, 7, "F7", AcquisitionChannelKind.Reference, "V"),
                new(2, 99, "Counter", AcquisitionChannelKind.SampleCounter, "count")],
            2, DateTimeOffset.UnixEpoch) { HardwareConfiguration = Metadata(profile) };
        var factory = new LocalAcquisitionRawWriterFactory(montageStore: store);
        await using var writer = await factory.CreateAsync(Guid.NewGuid(), metadata,
            new AcquisitionProjectContext("p", "p", "p", root, "{}"), Path.Combine(root, "recordings"), Token);
        Assert.True(Assert.Single(await store.LoadAsync(Token)).IsRecordingReferenced);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(writer.RecordingDirectory, "manifest.json")));
        Assert.Equal(JsonSerializer.Serialize(profile), manifest.RootElement.GetProperty("HardwareConfiguration")
            .GetProperty("montage_configuration_snapshot_json").GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(profile, Token));
    }

    private static Dictionary<string, string> Metadata(MontageProfile profile) => new()
    {
        ["montage_configuration_id"] = profile.Id,
        ["montage_configuration_snapshot_json"] = JsonSerializer.Serialize(profile),
    };

    [Fact]
    public async Task DraftOpenedBeforeRecordingCannotOverwriteOrDeleteNewlyReferencedId()
    {
        var store = Store();
        var profile = Profile();
        await store.SaveAsync(profile, Token);
        var channels = new ChannelConfigurationWorkspaceViewModel(new ChannelMappingViewModel(),
            new ChannelConfigurationProfileStore(Path.Combine(Root(), "channels.json")), montageStore: store);
        var workspace = new MontageConfigurationWorkspaceViewModel(channels, store) { SelectedProfile = profile };
        workspace.BeginEditSelected();
        Assert.True(workspace.CanEditDraft);
        await store.RegisterRecordingReferenceAsync(Metadata(profile), Token);
        workspace.DraftName = "旧草稿覆盖";
        await Assert.ThrowsAsync<InvalidOperationException>(workspace.SaveDraftAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(workspace.DeleteSelectedAsync);
        await workspace.RefreshReferencesAsync();
        Assert.False(workspace.CanEditDraft);
        Assert.Equal(profile.Name, Assert.Single(await store.LoadAsync(Token)).Name);
    }

    [Fact]
    public async Task ArchivedProfileIsExcludedFromAcquisitionButReferencedProfileRemainsAvailable()
    {
        var store = Store();
        var source = MontageAppearanceTests.Source() with { Id = Guid.NewGuid().ToString("N") };
        var profile = Profile() with { ChannelConfigurationSnapshot = source };
        await store.SaveAsync(profile, Token);
        var channelStore = new ChannelConfigurationProfileStore(Path.Combine(Root(), "channels.json"));
        await channelStore.SaveAsync(profile.ChannelConfigurationSnapshot, Token);
        var channels = new ChannelConfigurationWorkspaceViewModel(new ChannelMappingViewModel(), channelStore, montageStore: store);
        var workspace = new MontageConfigurationWorkspaceViewModel(channels, store);
        await store.RegisterRecordingReferenceAsync(Metadata(profile), Token);
        await workspace.RefreshAsync();
        Assert.Contains(workspace.AvailableForAcquisition, item => item.Id == profile.Id);
        await store.ArchiveAsync(profile.Id, Token);
        await workspace.RefreshReferencesAsync();
        Assert.DoesNotContain(workspace.AvailableForAcquisition, item => item.Id == profile.Id);
        Assert.Contains(workspace.Profiles, item => item.Id == profile.Id && item.IsArchived);
    }

    private static MontageProfile Profile()
    {
        var profile = MontageAppearanceTests.Profile();
        return profile with { Fingerprint = MontageProfileFingerprint.Create(profile.ChannelConfigurationFingerprint, profile.DerivedChannels) };
    }

    private static MontageProfileStore Store() => new(Path.Combine(Root(), "montages.json"));
    private static string Root() => Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N"));
}
