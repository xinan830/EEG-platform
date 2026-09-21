using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class ChannelConfigurationProfileTests
{
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
