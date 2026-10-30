
namespace BrainPlatform.Desktop.Tests.Acquisition;

using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.Views;

public sealed class ChannelConfigurationProfileTests
{
    [Fact]
    public async Task Store_RoundTripsElectrodeLocation_AndLegacyEntriesRemainUnavailable()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "profiles.json");
        var location = new ElectrodeLocationSnapshot("Fp1", -0.0294367, 0.0839171, -0.00699, "mri", "m", "mne.colin27_1020", "1.12.1");
        var profile = new ChannelConfigurationProfile(
            "profile-location", "位置", "test", ChannelConfigurationSource.User, "0:Reference",
            [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "Fp1", true, 0, location)],
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var store = new ChannelConfigurationProfileStore(path);
        await store.SaveAsync(profile, CancellationToken.None);
        var loaded = Assert.Single(await store.LoadAsync(CancellationToken.None));

        Assert.Equal(location, loaded.Channels.Single().ElectrodeLocation);
        var legacyPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "legacy.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        await File.WriteAllTextAsync(legacyPath, "[{\"Id\":\"old\",\"Name\":\"old\",\"Description\":\"\",\"Source\":1,\"DeviceSignature\":\"x\",\"Channels\":[{\"NativeChannelIndex\":0,\"ExpectedKind\":0,\"ElectrodeLabel\":\"Fp1\",\"IsSelectedForDisplay\":true,\"DisplayOrder\":0}],\"CreatedAtUtc\":\"2026-01-01T00:00:00+00:00\",\"UpdatedAtUtc\":\"2026-01-01T00:00:00+00:00\"}]");
        var legacy = Assert.Single(await new ChannelConfigurationProfileStore(legacyPath).LoadAsync(CancellationToken.None));
        Assert.Null(legacy.Channels.Single().ElectrodeLocation);
    }

    [Fact]
    public async Task Store_RoundTripsManualDiagramPositionSeparatelyFromStandardCoordinates()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "profiles.json");
        var position = new ElectrodeMapPosition(0.25, -0.4);
        var referencePosition = new ElectrodeMapPosition(0, 0.2);
        var groundPosition = new ElectrodeMapPosition(0, -0.92);
        var profile = new ChannelConfigurationProfile(
            "manual-position", "自定义位置", "test", ChannelConfigurationSource.User, "0:Reference",
            [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "MyRef", true, 0, ManualMapPosition: position)],
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            ReferenceMapPosition: referencePosition,
            GroundMapPosition: groundPosition);
        var store = new ChannelConfigurationProfileStore(path);

        ChannelConfigurationValidation.Validate(profile);
        await store.SaveAsync(profile, CancellationToken.None);
        var loaded = Assert.Single(await store.LoadAsync(CancellationToken.None));

        Assert.Equal(position, loaded.Channels.Single().ManualMapPosition);
        Assert.Null(loaded.Channels.Single().ElectrodeLocation);
        Assert.Equal(referencePosition, loaded.ReferenceMapPosition);
        Assert.Equal(groundPosition, loaded.GroundMapPosition);
    }

    [Fact]
    public void Validation_RejectsLocationThatContradictsConfiguredLabel()
    {
        var profile = new ChannelConfigurationProfile(
            "profile-location", "位置", "test", ChannelConfigurationSource.User, "0:Reference",
            [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "Fp1", true, 0,
                new ElectrodeLocationSnapshot("Fp2", 0, 0, 0, "mri", "m", "mne.colin27_1020", "1.12.1"))],
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var error = Assert.Throws<InvalidOperationException>(() => ChannelConfigurationValidation.Validate(profile));
        Assert.Contains("位置", error.Message);
    }

    [Fact]
    public void Validation_AllowsManualDiagramOverrideAlongsideStandardLocation()
    {
        var profile = new ChannelConfigurationProfile(
            "profile-location", "位置", "test", ChannelConfigurationSource.User, "0:Reference",
            [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "F3", true, 0,
                new ElectrodeLocationSnapshot("F3", -0.03, 0.04, 0, "mri", "m", "mne.colin27_1020", "1.12.1"),
                new ElectrodeMapPosition(0.45, -0.2))],
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        ChannelConfigurationValidation.Validate(profile);
    }

    [Fact]
    public void Fingerprint_IncludesElectrodeLocationSnapshot()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var location = new ElectrodeLocationSnapshot("Fp1", -0.029, 0.083, -0.006, "mri", "m", "mne.colin27_1020", "1.12.1");
        var withoutLocation = new ChannelConfigurationProfile(
            "profile-location", "位置", "test", ChannelConfigurationSource.User, "0:Reference",
            [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "Fp1", true, 0)], timestamp, timestamp);
        var withLocation = withoutLocation with
        {
            Channels = [withoutLocation.Channels.Single() with { ElectrodeLocation = location }]
        };

        Assert.NotEqual(
            ChannelConfigurationFingerprint.Create(withoutLocation),
            ChannelConfigurationFingerprint.Create(withLocation));
    }

    [Fact]
    public void Fingerprint_IgnoresManualDiagramPositionBecauseItIsDisplayOnly()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var profile = new ChannelConfigurationProfile(
            "manual-position", "自定义位置", "test", ChannelConfigurationSource.User, "0:Reference",
            [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "MyRef", true, 0)], timestamp, timestamp);
        var positioned = profile with
        {
            Channels = [profile.Channels.Single() with { ManualMapPosition = new ElectrodeMapPosition(0.2, -0.3) }]
        };

        Assert.Equal(ChannelConfigurationFingerprint.Create(profile), ChannelConfigurationFingerprint.Create(positioned));
    }

    [Fact]
    public async Task Store_RoundTripsOnlyUserChannelConfigurationMetadata()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "profiles.json");
        var profile = new ChannelConfigurationProfile(
            "profile-1",
            "我的配置",
            "test",
            ChannelConfigurationSource.User,
            "0:Reference;1:Reference",
            [
                new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "Fp1", true, 0),
                new ChannelConfigurationEntry(1, AcquisitionChannelKind.Reference, "Fp2", false, 1),
            ],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            HardwareReferenceElectrodeLocation: "FCz",
            HardwareGroundElectrodeLocation: "AFz");

        var store = new ChannelConfigurationProfileStore(path);
        await store.SaveAsync(profile, CancellationToken.None);

        var loaded = await store.LoadAsync(CancellationToken.None);
        var result = Assert.Single(loaded);
        Assert.Equal("我的配置", result.Name);
        Assert.Equal("Fp1", result.Channels[0].ElectrodeLabel);
        Assert.False(result.Channels[1].IsSelectedForDisplay);
        Assert.Equal("FCz", result.HardwareReferenceElectrodeLocation);
        Assert.Equal("AFz", result.HardwareGroundElectrodeLocation);
        Assert.Equal("REF：FCz · GND：AFz", result.HardwareWiringSummary);
    }

    [Fact]
    public async Task Store_MigratesLegacyUserProfileThatCollidesWithASystemPresetId()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "profiles.json");
        var store = new ChannelConfigurationProfileStore(path);
        var legacy = new ChannelConfigurationProfile(
            "system-ant-standard-28-input",
            "我的默认配置",
            "保留用户修改",
            ChannelConfigurationSource.User,
            "0:Reference",
            [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "AF3", false, 4)],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        await store.SaveAsync(legacy, CancellationToken.None);
        var migrated = Assert.Single(await store.LoadAsync(CancellationToken.None));

        Assert.NotEqual(legacy.Id, migrated.Id);
        Assert.True(Guid.TryParse(migrated.Id, out _));
        Assert.Equal("AF3", migrated.Channels.Single().ElectrodeLabel);
        Assert.False(migrated.Channels.Single().IsSelectedForDisplay);
    }

    [Fact]
    public void Mapping_RejectsProfileForDifferentPhysicalChannelKinds()
    {
        var mapping = new ChannelMappingViewModel(
            new ChannelLabelMappingStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json")));
        mapping.LoadDevice(new AcquisitionDeviceDescriptor(
            "device",
            "device",
            "serial",
            [500],
            ChannelCapabilities:
            [
                new AcquisitionChannelCapability(0, AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannelCapability(1, AcquisitionChannelKind.Reference, "V"),
            ]));
        var profile = new ChannelConfigurationProfile(
            "profile-1",
            "不匹配",
            "test",
            ChannelConfigurationSource.User,
            "0:Bipolar;1:Reference",
            [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var error = Assert.Throws<InvalidOperationException>(() => mapping.ApplyConfiguration(profile));

        Assert.Contains("不匹配", error.Message);
    }

    [Fact]
    public void Mapping_OffersSystemPresetOnlyForVerifiedDefaultPhysicalLayout()
    {
        var mapping = new ChannelMappingViewModel(
            new ChannelLabelMappingStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json")));
        var capabilities = Enumerable.Range(0, 24)
            .Select(index => new AcquisitionChannelCapability(index, AcquisitionChannelKind.Reference, "V"))
            .Concat(Enumerable.Range(24, 4).Select(index => new AcquisitionChannelCapability(index, AcquisitionChannelKind.Bipolar, "V")))
            .ToArray();
        mapping.LoadDevice(new AcquisitionDeviceDescriptor("device", "device", "serial", [500], ChannelCapabilities: capabilities));

        Assert.True(mapping.TryCreateDefaultSystemProfile(out var profile));
        Assert.Equal(ChannelConfigurationSource.System, profile.Source);
        Assert.Equal("Fp1", profile.Channels.Single(entry => entry.NativeChannelIndex == 0).ElectrodeLabel);
    }

    [Fact]
    public void Mapping_LoadDeviceDoesNotAutomaticallyApplySystemPreset()
    {
        var mapping = new ChannelMappingViewModel(
            new ChannelLabelMappingStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json")));

        mapping.LoadDevice(CreateDefaultDevice());

        Assert.Null(mapping.ActiveConfiguration);
        Assert.Null(mapping.ActiveConfigurationId);
        Assert.True(mapping.TryCreateDefaultSystemProfile(out var profile));
        Assert.Equal("system-ant-standard-28-input", profile.Id);
        Assert.Equal("ANT默认通道配置", profile.Name);
        Assert.Equal("查看", profile.OpenActionLabel);
        Assert.False(profile.CanEdit);
    }

    [Fact]
    public void Mapping_AppliesHardwareLocationsFromTheSelectedProfile()
    {
        var mapping = new ChannelMappingViewModel(
            new ChannelLabelMappingStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json")));
        var device = CreateDefaultDevice();
        mapping.LoadDevice(device);
        var profile = new ChannelConfigurationProfile(
            "profile-hardware-locations",
            "接线模板",
            "",
            ChannelConfigurationSource.User,
            mapping.GetDeviceSignature(),
            mapping.CaptureConfigurationEntries(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            HardwareReferenceElectrodeLocation: "FCz",
            HardwareGroundElectrodeLocation: "AFz");

        mapping.ApplyConfiguration(profile);

        Assert.Equal("FCz", mapping.CurrentHardwareReferenceElectrodeLocation);
        Assert.Equal("AFz", mapping.CurrentHardwareGroundElectrodeLocation);
    }

    [Fact]
    public void NewProfile_RequiresExplicitDeviceSelection_ThenUsesVerifiedTemplateAndUnits()
    {
        var workspace = CreateWorkspace();
        var device = CreateDefaultDevice();

        workspace.BeginNewProfile();

        Assert.True(workspace.IsDevicePickerVisible);
        Assert.Empty(workspace.DraftRows);

        workspace.DraftDevice = device;

        var fp1 = workspace.DraftRows.Single(row => row.NativeChannelIndex == 0);
        Assert.Equal("Fp1", fp1.ElectrodeLabel);
        Assert.Equal("V", fp1.DeviceUnit);
        Assert.Equal("µV", fp1.DisplayUnit);
        Assert.True(fp1.IsEnabled);
        Assert.Equal(28, workspace.DraftEegInputCount);
        Assert.Equal("REF", workspace.DraftReferenceElectrodeLocation);
        Assert.Equal("GND", workspace.DraftGroundElectrodeLocation);
    }

    [Fact]
    public void NewProfile_DevicePicker_DefersPhysicalMappingUntilSelectionIsConfirmed()
    {
        var workspace = CreateWorkspace();
        var device = CreateDefaultDevice();

        workspace.BeginNewProfile();
        workspace.PendingDeviceSelection = device;

        Assert.True(workspace.IsDevicePickerVisible);
        Assert.Null(workspace.DraftDevice);
        Assert.Empty(workspace.DraftRows);
        Assert.True(workspace.HasPendingDeviceSelection);

        workspace.ConfirmPendingDeviceSelection();

        Assert.False(workspace.IsDevicePickerVisible);
        Assert.Same(device, workspace.DraftDevice);
        Assert.NotEmpty(workspace.DraftRows);
        Assert.False(workspace.HasPendingDeviceSelection);
    }

    [Fact]
    public void EditingProfile_UsesIndependentDraftRows_AndDisplayToggleUpdatesOnlyTheDraft()
    {
        var workspace = CreateWorkspace();
        var device = CreateDefaultDevice();
        var original = new ChannelConfigurationProfile(
            "profile-a",
            "配置 A",
            "",
            ChannelConfigurationSource.User,
            "0:Reference",
            [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "Fp1", true, 0)],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            HardwareReferenceElectrodeLocation: "REF111",
            HardwareGroundElectrodeLocation: "GND111");
        var other = original with
        {
            Id = "profile-b",
            Name = "配置 B",
            Channels = [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "F3", true, 0)],
        };
        workspace.Profiles.Add(original);
        workspace.Profiles.Add(other);
        workspace.SelectedProfile = original;

        workspace.BeginEditSelected();
        workspace.DraftDevice = device;
        Assert.Equal("REF111", workspace.DraftReferenceElectrodeLocation);
        Assert.Equal("GND111", workspace.DraftGroundElectrodeLocation);
        var draft = workspace.DraftRows.Single(row => row.NativeChannelIndex == 0);
        var enabledBeforeToggle = workspace.DraftEnabledCount;
        draft.ElectrodeLabel = "AF3";
        draft.IsEnabled = false;

        Assert.Equal("Fp1", original.Channels.Single().ElectrodeLabel);
        Assert.Equal("F3", other.Channels.Single().ElectrodeLabel);
        Assert.True(original.Channels.Single().IsSelectedForDisplay);
        Assert.Equal(enabledBeforeToggle - 1, workspace.DraftEnabledCount);
    }

    [Fact]
    public async Task DraftSaveVisibilityTracksDifferencesFromSavedBaseline()
    {
        var store = new ChannelConfigurationProfileStore(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "profiles.json"));
        var workspace = new ChannelConfigurationWorkspaceViewModel(
            new ChannelMappingViewModel(
                new ChannelLabelMappingStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"))),
            store);
        var device = CreateDefaultDevice();
        var profile = new ChannelConfigurationProfile(
            Guid.NewGuid().ToString("N"), "原配置", "原说明", ChannelConfigurationSource.User,
            AcquisitionDeviceCompatibility.CreateSignature(device.DriverId, device.ChannelCapabilities),
            device.ChannelCapabilities!.Select(capability => new ChannelConfigurationEntry(
                capability.NativeChannelIndex,
                capability.Kind,
                capability.NativeChannelIndex == 0 ? "Fp1" : capability.Kind == AcquisitionChannelKind.Reference
                    ? $"E{capability.NativeChannelIndex}"
                    : string.Empty,
                capability.Kind == AcquisitionChannelKind.Reference,
                capability.NativeChannelIndex)).ToArray(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            device.DisplayName, AcquisitionDeviceCompatibility.NormalizeDriverId(device.DriverId));
        await store.SaveAsync(profile, CancellationToken.None);
        await workspace.RefreshAsync();
        workspace.SelectedProfile = Assert.Single(workspace.Profiles);

        workspace.BeginEditSelected();
        workspace.DraftDevice = device;
        Assert.False(workspace.HasUnsavedChanges);

        workspace.DraftName = "新名称";
        Assert.True(workspace.HasUnsavedChanges);
        workspace.DraftName = "原配置";
        Assert.False(workspace.HasUnsavedChanges);

        workspace.DraftDescription = "修改说明";
        await workspace.SaveDraftAsync();
        Assert.False(workspace.HasUnsavedChanges);
        Assert.True(workspace.IsDraftOpen);

        workspace.DraftRows[0].ElectrodeLabel = "F3";
        Assert.True(workspace.HasUnsavedChanges);
        workspace.DraftRows[0].ElectrodeLabel = "Fp1";
        Assert.False(workspace.HasUnsavedChanges);

        workspace.DraftRows[0].SetManualMapPosition(new ElectrodeMapPosition(0.1, 0.1));
        Assert.True(workspace.HasUnsavedChanges);
        workspace.DraftRows[0].SetManualMapPosition(null);
        Assert.False(workspace.HasUnsavedChanges);

        workspace.DraftRows[0].IsEnabled = false;
        Assert.True(workspace.HasUnsavedChanges);
        workspace.DraftRows[0].IsEnabled = true;
        Assert.False(workspace.HasUnsavedChanges);
    }

    [Theory]
    [InlineData(System.Windows.MessageBoxResult.Yes, true, false)]
    [InlineData(System.Windows.MessageBoxResult.No, false, false)]
    [InlineData(System.Windows.MessageBoxResult.Cancel, false, true)]
    public async Task LeavingDraft_AppliesSaveDiscardAndCancelChoices(
        System.Windows.MessageBoxResult choice,
        bool shouldSave,
        bool shouldRemainOpen)
    {
        var saveCount = 0;
        var closeCount = 0;
        var errorCount = 0;

        var canLeave = await ChannelDraftLeaveGuard.ConfirmAndCloseAsync(
            true,
            () => true,
            () => choice,
            () => { saveCount++; return Task.CompletedTask; },
            () => closeCount++,
            _ => errorCount++);

        Assert.Equal(!shouldRemainOpen, canLeave);
        Assert.Equal(shouldSave ? 1 : 0, saveCount);
        Assert.Equal(shouldRemainOpen ? 0 : 1, closeCount);
        Assert.Equal(0, errorCount);
    }

    [Fact]
    public async Task LeavingDraft_SaveFailureKeepsDraftOpen()
    {
        var closeCount = 0;
        var errorCount = 0;

        var canLeave = await ChannelDraftLeaveGuard.ConfirmAndCloseAsync(
            true,
            () => true,
            () => System.Windows.MessageBoxResult.Yes,
            () => Task.FromException(new IOException("store unavailable")),
            () => closeCount++,
            _ => errorCount++);

        Assert.False(canLeave);
        Assert.Equal(0, closeCount);
        Assert.Equal(1, errorCount);
    }

    [Fact]
    public async Task LeavingUnchangedDraftClosesWithoutPrompt()
    {
        var promptCount = 0;
        var closeCount = 0;

        var canLeave = await ChannelDraftLeaveGuard.ConfirmAndCloseAsync(
            true,
            () => false,
            () => { promptCount++; return System.Windows.MessageBoxResult.Cancel; },
            () => Task.CompletedTask,
            () => closeCount++,
            _ => { });

        Assert.True(canLeave);
        Assert.Equal(0, promptCount);
        Assert.Equal(1, closeCount);
    }

    [Fact]
    public void Draft_BulkDisplayCommands_UpdateAllEegRows()
    {
        var workspace = CreateWorkspace();
        workspace.BeginNewProfile();
        workspace.DraftDevice = CreateDefaultDevice();

        var showAll = Assert.IsAssignableFrom<System.Windows.Input.ICommand>(workspace.ShowAllDraftChannelsCommand);
        var hideAll = Assert.IsAssignableFrom<System.Windows.Input.ICommand>(workspace.HideAllDraftChannelsCommand);

        Assert.True(hideAll.CanExecute(null));
        hideAll.Execute(null);
        Assert.Equal(0, workspace.DraftEnabledCount);
        Assert.All(workspace.DraftRows.Where(row => row.IsEegInput), row => Assert.False(row.IsEnabled));
        Assert.True(showAll.CanExecute(null));

        showAll.Execute(null);
        Assert.Equal(workspace.DraftEegInputCount, workspace.DraftEnabledCount);
        Assert.All(workspace.DraftRows.Where(row => row.IsEegInput), row => Assert.True(row.IsEnabled));
    }

    [Fact]
    public async Task Draft_SavesOperatorRecordedHardwareReferenceAndGroundLocations()
    {
        var store = new ChannelConfigurationProfileStore(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "profiles.json"));
        var workspace = new ChannelConfigurationWorkspaceViewModel(
            new ChannelMappingViewModel(
                new ChannelLabelMappingStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"))),
            store);

        workspace.BeginNewProfile();
        workspace.DraftDevice = CreateDefaultDevice();
        workspace.DraftName = "含硬件接线的配置";
        workspace.DraftReferenceElectrodeLocation = "FCz";
        workspace.DraftGroundElectrodeLocation = "AFz";

        await workspace.SaveDraftAsync();

        var saved = Assert.Single(await store.LoadAsync(CancellationToken.None));
        Assert.Equal("FCz", saved.HardwareReferenceElectrodeLocation);
        Assert.Equal("AFz", saved.HardwareGroundElectrodeLocation);
    }

    [Fact]
    public async Task ActiveStore_RoundTripsAppliedProfileSnapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "active.json");
        var store = new ActiveChannelConfigurationStore(path);
        var profile = new ChannelConfigurationProfile(
            "profile-active",
            "采集配置",
            "",
            ChannelConfigurationSource.User,
            "device-signature",
            [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "F3", true, 0)],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            HardwareReferenceElectrodeLocation: "FCz",
            HardwareGroundElectrodeLocation: "AFz");

        await store.SaveAsync(profile, CancellationToken.None);

        Assert.True(store.TryLoad("device-signature", out var active));
        Assert.Equal("profile-active", active.Profile.Id);
        Assert.Equal("F3", active.Profile.Channels.Single().ElectrodeLabel);
        Assert.Equal("FCz", active.Profile.HardwareReferenceElectrodeLocation);
    }

    [Fact]
    public async Task Mapping_LoadDeviceDoesNotRestoreGlobalAppliedProfileSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var device = CreateDefaultDevice();
        var labels = new ChannelLabelMappingStore(Path.Combine(root, "labels.json"));
        var display = new ChannelDisplaySelectionStore(Path.Combine(root, "display.json"));
        var active = new ActiveChannelConfigurationStore(Path.Combine(root, "active.json"));
        var mapping = new ChannelMappingViewModel(labels, displaySelectionStore: display, activeConfigurationStore: active);
        mapping.LoadDevice(device);
        var profile = new ChannelConfigurationProfile(
            "profile-active",
            "采集配置",
            "",
            ChannelConfigurationSource.User,
            mapping.GetDeviceSignature(),
            mapping.CaptureConfigurationEntries(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            HardwareReferenceElectrodeLocation: "FCz",
            HardwareGroundElectrodeLocation: "AFz");
        await active.SaveAsync(profile, CancellationToken.None);

        var reloaded = new ChannelMappingViewModel(labels, displaySelectionStore: display, activeConfigurationStore: active);
        reloaded.LoadDevice(device);

        Assert.Null(reloaded.ActiveConfigurationId);
        Assert.Equal("REF", reloaded.CurrentHardwareReferenceElectrodeLocation);
        Assert.Equal("GND", reloaded.CurrentHardwareGroundElectrodeLocation);
    }

    [Fact]
    public async Task Refresh_CountsGeneratedSystemMontagesAsReferencesToTheDefaultChannelProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var mapping = new ChannelMappingViewModel(
            new ChannelLabelMappingStore(Path.Combine(root, "labels.json")));
        mapping.LoadDevice(CreateDefaultDevice());
        var workspace = new ChannelConfigurationWorkspaceViewModel(
            mapping,
            new ChannelConfigurationProfileStore(Path.Combine(root, "channels.json")),
            montageStore: new MontageProfileStore(Path.Combine(root, "montages.json")));

        await workspace.RefreshAsync();

        var systemProfile = Assert.Single(workspace.Profiles);
        Assert.Equal(ChannelConfigurationSource.System, systemProfile.Source);
        Assert.Equal(6, systemProfile.MontageReferenceCount);
        Assert.Equal("已被 6 个导联引用 · 通道配置已锁定", systemProfile.ReferenceSummary);
        Assert.Contains("标准电极位置服务尚未连接", workspace.StatusText);
    }

    private static ChannelConfigurationWorkspaceViewModel CreateWorkspace() => new(
        new ChannelMappingViewModel(
            new ChannelLabelMappingStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"))),
        new ChannelConfigurationProfileStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "profiles.json")));

    private static AcquisitionDeviceDescriptor CreateDefaultDevice()
    {
        var capabilities = Enumerable.Range(0, 24)
            .Select(index => new AcquisitionChannelCapability(index, AcquisitionChannelKind.Reference, "V"))
            .Concat(Enumerable.Range(24, 4).Select(index => new AcquisitionChannelCapability(index, AcquisitionChannelKind.Bipolar, "V")))
            .ToArray();
        return new AcquisitionDeviceDescriptor("device", "ANT", "serial", [500], ChannelCapabilities: capabilities);
    }
}
