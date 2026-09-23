
namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class ConfigurationChainClosureTests
{
    [Fact]
    public async Task Draft_RejectsDuplicateLabelsBeforeSavingCatalogueEntry()
    {
        var root = CreateTemporaryDirectory();
        var profileStore = new ChannelConfigurationProfileStore(Path.Combine(root, "profiles.json"));
        var workspace = new ChannelConfigurationWorkspaceViewModel(
            CreateMapping(root),
            profileStore);
        workspace.BeginNewProfile();
        workspace.DraftDevice = CreateDefaultDevice();
        workspace.DraftName = "重复标签";
        workspace.DraftRows[0].ElectrodeLabel = "F3";
        workspace.DraftRows[1].ElectrodeLabel = "f3";
        workspace.DraftRows[0].IsEnabled = true;
        workspace.DraftRows[1].IsEnabled = true;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(workspace.SaveDraftAsync);

        Assert.Contains("电极标签不能重复", error.Message);
        Assert.Empty(await profileStore.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Apply_InvalidProfile_DoesNotReplaceCurrentSnapshot()
    {
        var root = CreateTemporaryDirectory();
        var mapping = CreateMapping(root);
        mapping.LoadDevice(CreateDefaultDevice());
        Assert.True(mapping.TryCreateDefaultSystemProfile(out var original));
        mapping.ApplyConfiguration(original);
        var invalid = original with
        {
            Id = Guid.NewGuid().ToString("N"),
            Source = ChannelConfigurationSource.User,
            Channels = original.Channels.Select(channel => channel with { IsSelectedForDisplay = false }).ToArray(),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mapping.ApplyAndSaveConfigurationAsync(invalid, CancellationToken.None));

        Assert.Equal(original.Id, mapping.ActiveConfigurationId);
        Assert.Equal(
            ChannelConfigurationFingerprint.Create(original),
            ChannelConfigurationFingerprint.Create(mapping.ActiveConfiguration!));
    }

    [Fact]
    public async Task EditedAppliedProfile_IsMarkedAsUnappliedInsteadOfCurrent()
    {
        var root = CreateTemporaryDirectory();
        var mapping = CreateMapping(root);
        mapping.LoadDevice(CreateDefaultDevice());
        Assert.True(mapping.TryCreateDefaultSystemProfile(out var system));
        var applied = system with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "用户配置",
            Source = ChannelConfigurationSource.User,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        await mapping.ApplyAndSaveConfigurationAsync(applied, CancellationToken.None);
        var profileStore = new ChannelConfigurationProfileStore(Path.Combine(root, "profiles.json"));
        await profileStore.SaveAsync(applied, CancellationToken.None);
        var edited = applied with
        {
            Channels = applied.Channels.Select((channel, index) => index == 0
                ? channel with { ElectrodeLabel = "AFp1" }
                : channel).ToArray(),
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddSeconds(1),
        };
        await profileStore.SaveAsync(edited, CancellationToken.None);
        var workspace = new ChannelConfigurationWorkspaceViewModel(mapping, profileStore);

        await workspace.RefreshAsync();

        var displayed = Assert.Single(workspace.Profiles, profile => profile.Id == applied.Id);
        Assert.False(displayed.IsActive);
        Assert.False(displayed.HasUnappliedChanges);
        Assert.Equal("可用于导联", displayed.StatusLabel);
        Assert.True(displayed.CanDelete);
    }

    [Fact]
    public async Task Montage_ReportsWhenItsChannelSnapshotIsOutOfDate()
    {
        var root = CreateTemporaryDirectory();
        var original = CreateSmallProfile("F3");
        var edited = original with
        {
            Channels = [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "AF3", true, 0)],
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddSeconds(1),
        };
        var channelStore = new ChannelConfigurationProfileStore(Path.Combine(root, "channels.json"));
        await channelStore.SaveAsync(edited, CancellationToken.None);
        var channelWorkspace = new ChannelConfigurationWorkspaceViewModel(CreateMapping(root), channelStore);
        var montage = CreateOriginalMontage(original);
        var montageStore = new MontageProfileStore(Path.Combine(root, "montages.json"));
        await montageStore.SaveAsync(montage, CancellationToken.None);
        var montageWorkspace = new MontageConfigurationWorkspaceViewModel(channelWorkspace, montageStore);

        await montageWorkspace.RefreshAsync();

        var displayed = Assert.Single(montageWorkspace.Profiles);
        Assert.Equal("来源已修改", displayed.ChannelSnapshotStatusLabel);
        Assert.Equal("F3", displayed.ChannelConfigurationSnapshot.Channels.Single().ElectrodeLabel);
    }

    [Fact]
    public async Task Montage_RejectsAStoredFingerprintThatDoesNotMatchItsSnapshot()
    {
        var root = CreateTemporaryDirectory();
        var source = CreateSmallProfile("F3");
        var channelStore = new ChannelConfigurationProfileStore(Path.Combine(root, "channels.json"));
        await channelStore.SaveAsync(source, CancellationToken.None);
        var channelWorkspace = new ChannelConfigurationWorkspaceViewModel(CreateMapping(root), channelStore);
        var montageStore = new MontageProfileStore(Path.Combine(root, "montages.json"));
        await montageStore.SaveAsync(
            CreateOriginalMontage(source) with { ChannelConfigurationFingerprint = "tampered" },
            CancellationToken.None);
        var montageWorkspace = new MontageConfigurationWorkspaceViewModel(channelWorkspace, montageStore);

        await montageWorkspace.RefreshAsync();

        Assert.Equal("配置无效", Assert.Single(montageWorkspace.Profiles).ChannelSnapshotStatusLabel);
    }

    [Fact]
    public async Task ReferencedProfile_AllowsMetadataButRejectsSignalChangesAndDeletion()
    {
        var root = CreateTemporaryDirectory();
        var source = CreateSmallProfile("F3");
        var montageStore = new MontageProfileStore(Path.Combine(root, "montages.json"));
        await montageStore.SaveAsync(CreateOriginalMontage(source), CancellationToken.None);
        var policy = new ChannelConfigurationReferencePolicy(montageStore);
        var remapped = source with
        {
            Channels = [source.Channels.Single() with { ElectrodeLabel = "AF3" }],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.EnsureSignalChangeAllowedAsync(source, remapped, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.EnsureDeleteAllowedAsync(source, CancellationToken.None));
        await policy.EnsureSignalChangeAllowedAsync(
            source,
            source with { Name = "新名称", Description = "新说明" },
            CancellationToken.None);
    }

    [Fact]
    public async Task ReferencedProfile_IsLockedAndCanBeSavedAsANewEditableVersion()
    {
        var root = CreateTemporaryDirectory();
        var mapping = CreateMapping(root);
        mapping.LoadDevice(CreateDefaultDevice());
        Assert.True(mapping.TryCreateDefaultSystemProfile(out var system));
        var source = system with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "用户通道配置",
            Source = ChannelConfigurationSource.User,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var channelStore = new ChannelConfigurationProfileStore(Path.Combine(root, "channels.json"));
        await channelStore.SaveAsync(source, CancellationToken.None);
        var montageStore = new MontageProfileStore(Path.Combine(root, "montages.json"));
        await montageStore.SaveAsync(CreateOriginalMontage(source), CancellationToken.None);
        var workspace = new ChannelConfigurationWorkspaceViewModel(
            mapping,
            channelStore,
            montageStore: montageStore);
        await workspace.RefreshAsync();
        workspace.SelectedProfile = Assert.Single(workspace.Profiles, profile => profile.Id == source.Id);

        workspace.BeginEditSelected();

        Assert.True(workspace.CanEditDraft);
        Assert.False(workspace.CanEditSignalDraft);
        Assert.Equal(1, workspace.SelectedProfile.MontageReferenceCount);
        Assert.Equal("查看", workspace.SelectedProfile.OpenActionLabel);
        Assert.Equal("已被 1 个导联引用 · 通道配置已锁定", workspace.SelectedProfile.ReferenceSummary);
        Assert.False(workspace.SelectedProfile.CanDelete);

        workspace.CloseDraft();
        workspace.BeginNewVersionSelected();
        Assert.True(workspace.CanEditSignalDraft);
        workspace.DraftName = "用户通道配置 v2";
        workspace.DraftRows[0].ElectrodeLabel = "AFp1";
        await workspace.SaveDraftAsync();

        var stored = await channelStore.LoadAsync(CancellationToken.None);
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, profile => profile.Id == source.Id && profile.Channels[0].ElectrodeLabel == "Fp1");
        Assert.Contains(stored, profile => profile.Id != source.Id && profile.Channels[0].ElectrodeLabel == "AFp1");
    }

    private static ChannelMappingViewModel CreateMapping(string root) => new(
        new ChannelLabelMappingStore(Path.Combine(root, "labels.json")),
        displaySelectionStore: new ChannelDisplaySelectionStore(Path.Combine(root, "display.json")),
        activeConfigurationStore: new ActiveChannelConfigurationStore(Path.Combine(root, "active.json")));

    private static ChannelConfigurationProfile CreateSmallProfile(string label) => new(
        "11111111111111111111111111111111", "通道配置", "", ChannelConfigurationSource.User,
        "ant-eego|one-reference",
        [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, label, true, 0)],
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static MontageProfile CreateOriginalMontage(ChannelConfigurationProfile source)
    {
        var first = source.Channels.First(channel => channel.IsSelectedForDisplay);
        var derived = new[]
        {
            new DerivedMontageChannel(
                $"{first.ElectrodeLabel}-REF",
                first.ElectrodeLabel,
                MontageNegativeKind.OriginalHardwareReference,
                [],
                0),
        };
        var channelFingerprint = ChannelConfigurationFingerprint.Create(source);
        return new MontageProfile(
            Guid.NewGuid().ToString("N"), "快照导联", "", MontageProfileSource.User,
            source.DeviceSignature, channelFingerprint, source, derived, 1,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            MontageProfileFingerprint.Create(channelFingerprint, derived));
    }

    private static AcquisitionDeviceDescriptor CreateDefaultDevice()
    {
        var capabilities = Enumerable.Range(0, 24)
            .Select(index => new AcquisitionChannelCapability(index, AcquisitionChannelKind.Reference, "V"))
            .Concat(Enumerable.Range(24, 4)
                .Select(index => new AcquisitionChannelCapability(index, AcquisitionChannelKind.Bipolar, "V")))
            .ToArray();
        return new AcquisitionDeviceDescriptor(
            "device", "ANT", "serial", [500],
            ChannelCapabilities: capabilities,
            Model: "EE-511",
            DriverId: "ant-eego");
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
