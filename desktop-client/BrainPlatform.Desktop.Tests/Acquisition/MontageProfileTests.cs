using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class MontageProfileTests
{
    [Fact]
    public void Validation_AcceptsOriginalSpecifiedChannelAndCommonAverageReference()
    {
        var profile = CreateChannelConfiguration();
        var average = new[] { "F3", "F4", "Cz", "M1", "M2" };
        var channels = new[]
        {
            new DerivedMontageChannel("F3-REF", "F3", MontageNegativeKind.OriginalHardwareReference, [], 0),
            new DerivedMontageChannel("F4-Cz", "F4", MontageNegativeKind.Channel, ["Cz"], 1),
            new DerivedMontageChannel("Cz-AVG", "Cz", MontageNegativeKind.Mean, average, 2),
            new DerivedMontageChannel("M1-REF", "M1", MontageNegativeKind.OriginalHardwareReference, [], 3),
            new DerivedMontageChannel("M2-REF", "M2", MontageNegativeKind.OriginalHardwareReference, [], 4),
        };

        MontageValidation.Validate(profile, channels, average);
    }

    [Fact]
    public void Validation_RejectsGroundLikeOrHiddenSignalNotInSnapshot()
    {
        var channels = CreateOriginalRows();
        channels[0] = new DerivedMontageChannel("F3-GND", "F3", MontageNegativeKind.Channel, ["GND"], 0);

        var error = Assert.Throws<InvalidOperationException>(() =>
            MontageValidation.Validate(CreateChannelConfiguration(), channels));

        Assert.Contains("GND", error.Message);
    }

    [Fact]
    public void Validation_RejectsPartialSourceRows()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            MontageValidation.Validate(CreateChannelConfiguration(), CreateOriginalRows().Take(4).ToArray()));

        Assert.Contains("一一对应", error.Message);
    }

    [Fact]
    public void Fingerprint_ChangesWhenSignalMappingChanges_ButNotWhenProfileNameChanges()
    {
        var source = CreateChannelConfiguration();
        var renamed = source with { Name = "另一个名字" };
        var remapped = source with
        {
            Channels = source.Channels.Select(channel => channel with
            {
                ElectrodeLabel = channel.ElectrodeLabel == "F3" ? "AF3" : channel.ElectrodeLabel,
            }).ToArray(),
        };

        Assert.Equal(ChannelConfigurationFingerprint.Create(source), ChannelConfigurationFingerprint.Create(renamed));
        Assert.NotEqual(ChannelConfigurationFingerprint.Create(source), ChannelConfigurationFingerprint.Create(remapped));
    }

    [Fact]
    public void MontageFingerprint_ChangesWhenTheAverageReferenceGroupChanges()
    {
        var channels = CreateOriginalRows();
        Assert.NotEqual(
            MontageProfileFingerprint.Create("channels", channels, ["F3", "F4"]),
            MontageProfileFingerprint.Create("channels", channels, ["F3", "F4", "Cz"]));
    }

    [Fact]
    public async Task Store_RoundTripsChannelSnapshotAndAverageReferenceGroup()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "montages.json");
        var snapshot = CreateChannelConfiguration();
        var average = new[] { "F3", "F4", "Cz", "M1", "M2" };
        var channels = CreateOriginalRows();
        channels[0] = new DerivedMontageChannel("F3-AVG", "F3", MontageNegativeKind.Mean, average, 0);
        var profile = new MontageProfile(
            "montage-1", "平均参考", "test", MontageProfileSource.User,
            snapshot.DeviceSignature, ChannelConfigurationFingerprint.Create(snapshot), snapshot,
            channels, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            AverageReferenceLabels: average);

        var store = new MontageProfileStore(path);
        await store.SaveAsync(profile, CancellationToken.None);
        var loaded = Assert.Single(await store.LoadAsync(CancellationToken.None));

        Assert.Equal("通道配置", loaded.ChannelConfigurationSnapshot.Name);
        Assert.Equal(average, loaded.AverageReferenceLabels);
        Assert.Equal("F3 - Mean(F3, F4, Cz, M1, M2)", loaded.DerivedChannels.First().FormulaText);
    }

    [Fact]
    public void Workspace_GeneratesOneFixedRowPerConfiguredSourceChannel()
    {
        var snapshot = CreateChannelConfiguration();
        var channelWorkspace = new ChannelConfigurationWorkspaceViewModel(
            new ChannelMappingViewModel(new ChannelLabelMappingStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"))),
            new ChannelConfigurationProfileStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "channels.json")));
        channelWorkspace.Profiles.Add(snapshot);
        var workspace = new MontageConfigurationWorkspaceViewModel(channelWorkspace);

        workspace.BeginNewProfile();

        Assert.Equal(5, workspace.DraftRows.Count);
        Assert.Equal("F3", workspace.DraftRows[0].PositiveLabel);
        Assert.Equal("F3-REF", workspace.DraftRows[0].Name);
        Assert.Equal(5, workspace.AverageReferenceChannels.Count(option => option.IsIncluded));
    }

    [Fact]
    public void Workspace_AppliesAUserSelectedAverageGroupToEveryFixedRow()
    {
        var workspace = CreateWorkspaceWithChannelConfiguration();
        workspace.BeginNewProfile();
        foreach (var option in workspace.AverageReferenceChannels)
        {
            option.IsIncluded = option.Label is "M1" or "M2";
        }

        workspace.ApplyAverageReferenceToAll();

        Assert.All(workspace.DraftRows, row => Assert.Equal(MontageNegativeKind.Mean, row.NegativeKind));
        Assert.Equal(["M1", "M2"], workspace.AverageReferenceChannels
            .Where(option => option.IsIncluded)
            .Select(option => option.Label));
    }

    [Fact]
    public void Workspace_AppliesSpecifiedPairOnlyWhenExactlyTwoChannelsAreSelected()
    {
        var workspace = CreateWorkspaceWithChannelConfiguration();
        workspace.BeginNewProfile();
        foreach (var option in workspace.AverageReferenceChannels)
        {
            option.IsIncluded = option.Label is "M1" or "M2";
        }

        workspace.ApplySpecifiedPairToAll();

        Assert.All(workspace.DraftRows, row => Assert.Equal(MontageNegativeKind.SpecifiedPair, row.NegativeKind));
        Assert.Equal("双参考（M1、M2）", workspace.SpecifiedPairSummary);
    }

    [Fact]
    public void DraftRow_ExposesTwoIndependentPairSelectionsAndPersistsThem()
    {
        var row = new MontageDraftRow(
            "F3",
            0,
            ["F3", "M1", "M2", "A1", "A2"],
            MontageNegativeKind.SpecifiedPair,
            null,
            "M1",
            "M2",
            "AVG（2 通道）",
            "双参考（M1、M2）");

        Assert.True(row.IsSpecifiedPairReference);
        Assert.Contains("M1", row.PairFirstOptions);
        Assert.Contains("M2", row.PairSecondOptions);
        Assert.DoesNotContain("M1", row.PairSecondOptions);

        row.SelectedPairFirstLabel = "A1";
        row.SelectedPairSecondLabel = "A2";
        var model = row.ToModel(["M1", "M2"]);

        Assert.Equal(["A1", "A2"], model.NegativeLabels);
        Assert.Equal("F3 - Mean(A1, A2)", model.FormulaText);
    }

    [Fact]
    public void SystemMontageFactory_CreatesNamedReadOnlyAntPresets()
    {
        var source = CreateAntSystemChannelConfiguration();
        var profiles = SystemMontageProfileFactory.Create(source);

        Assert.Equal(
            ["ANT默认通道配置 REF参考", "ANT默认通道配置 平均参考", "ANT默认通道配置 M1/M2参考",
                "ANT默认通道配置 Cz参考", "ANT默认通道配置 纵向双极", "ANT默认通道配置 横向双极"],
            profiles.Select(profile => profile.Name));
        Assert.All(profiles, profile =>
        {
            Assert.Equal(MontageProfileSource.System, profile.Source);
            Assert.Equal("系统默认", profile.SourceLabel);
            Assert.Equal("查看", profile.OpenActionLabel);
            Assert.False(profile.CanEdit);
            Assert.False(profile.CanCopy);
            Assert.False(profile.CanDelete);
        });
        Assert.Contains(profiles.Single(profile => profile.Name.EndsWith("M1/M2参考"))
            .DerivedChannels, channel => channel.FormulaText.Contains("Mean(M1, M2)"));
        Assert.Contains(profiles.Single(profile => profile.Name.EndsWith("Cz参考"))
            .DerivedChannels, channel => channel.FormulaText == "F3 - Cz");
        Assert.Contains(profiles.Single(profile => profile.Name.EndsWith("纵向双极"))
            .DerivedChannels, channel => channel.Name == "Fp1-F7");
        Assert.Contains(profiles.Single(profile => profile.Name.EndsWith("横向双极"))
            .DerivedChannels, channel => channel.Name == "Fp1-Fp2");
        Assert.Equal(21, profiles.Single(profile => profile.Name.EndsWith("REF参考")).ChannelCount);
        Assert.Equal(21, profiles.Single(profile => profile.Name.EndsWith("平均参考")).ChannelCount);
        Assert.Equal(21, profiles.Single(profile => profile.Name.EndsWith("M1/M2参考")).ChannelCount);
        Assert.Equal(20, profiles.Single(profile => profile.Name.EndsWith("Cz参考")).ChannelCount);
        Assert.Equal(16, profiles.Single(profile => profile.Name.EndsWith("纵向双极")).ChannelCount);
        Assert.Equal(14, profiles.Single(profile => profile.Name.EndsWith("横向双极")).ChannelCount);
        Assert.Equal("指定通道", profiles.Single(profile => profile.Name.EndsWith("纵向双极")).ReferenceSummary);
        Assert.All(profiles, profile => MontageValidation.Validate(
            profile.ChannelConfigurationSnapshot,
            profile.DerivedChannels,
            profile.AverageReferenceLabels));
    }

    [Fact]
    public void Workspace_OpensSystemMontageAsReadOnlyViewWithItsActualRows()
    {
        var source = CreateAntSystemChannelConfiguration();
        var systemProfile = SystemMontageProfileFactory.Create(source)
            .Single(profile => profile.Name.EndsWith("纵向双极"));
        var channelWorkspace = new ChannelConfigurationWorkspaceViewModel(
            new ChannelMappingViewModel(new ChannelLabelMappingStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"))),
            new ChannelConfigurationProfileStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "channels.json")));
        channelWorkspace.Profiles.Add(source);
        var workspace = new MontageConfigurationWorkspaceViewModel(channelWorkspace)
        {
            SelectedProfile = systemProfile,
        };

        workspace.BeginEditSelected();

        Assert.False(workspace.CanEditDraft);
        Assert.Equal("查看导联配置", workspace.DraftTitle);
        Assert.Equal(systemProfile.ChannelCount, workspace.DraftRows.Count);
        Assert.All(workspace.DraftRows, row => Assert.Equal(MontageNegativeKind.Channel, row.NegativeKind));
        Assert.False(workspace.ShowReferenceGroup);
    }

    [Fact]
    public async Task Workspace_ReloadsSystemMontagesWhenDeviceChannelsArriveAfterInitialRefresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N"));
        var mapping = new ChannelMappingViewModel(
            new ChannelLabelMappingStore(Path.Combine(root, "labels.json")),
            displaySelectionStore: new ChannelDisplaySelectionStore(Path.Combine(root, "display.json")),
            activeConfigurationStore: new ActiveChannelConfigurationStore(Path.Combine(root, "active.json")));
        var montageStore = new MontageProfileStore(Path.Combine(root, "montages.json"));
        var channelWorkspace = new ChannelConfigurationWorkspaceViewModel(
            mapping,
            new ChannelConfigurationProfileStore(Path.Combine(root, "channels.json")),
            montageStore: montageStore);
        var workspace = new MontageConfigurationWorkspaceViewModel(channelWorkspace, montageStore);
        await workspace.RefreshAsync();
        Assert.Empty(workspace.Profiles);

        var capabilities = Enumerable.Range(0, 24)
            .Select(index => new AcquisitionChannelCapability(index, AcquisitionChannelKind.Reference, "V"))
            .Concat(Enumerable.Range(24, 4)
                .Select(index => new AcquisitionChannelCapability(index, AcquisitionChannelKind.Bipolar, "V")))
            .ToArray();
        mapping.LoadDevice(new AcquisitionDeviceDescriptor(
            "device", "ANT/eego EE-511", "serial", [500, 1_000, 4_000],
            ChannelCapabilities: capabilities,
            Model: "EE-511",
            DriverId: "ant-eego"));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (workspace.Profiles.Count == 0)
        {
            await Task.Delay(10, timeout.Token);
        }

        Assert.Single(workspace.AvailableChannelConfigurations);
        Assert.Equal(6, workspace.Profiles.Count);
        Assert.Equal(6, workspace.AvailableForAcquisition.Count);
    }

    private static MontageConfigurationWorkspaceViewModel CreateWorkspaceWithChannelConfiguration()
    {
        var channelWorkspace = new ChannelConfigurationWorkspaceViewModel(
            new ChannelMappingViewModel(new ChannelLabelMappingStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"))),
            new ChannelConfigurationProfileStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "channels.json")));
        channelWorkspace.Profiles.Add(CreateChannelConfiguration());
        return new MontageConfigurationWorkspaceViewModel(channelWorkspace);
    }

    private static DerivedMontageChannel[] CreateOriginalRows() =>
    [
        new("F3-REF", "F3", MontageNegativeKind.OriginalHardwareReference, [], 0),
        new("F4-REF", "F4", MontageNegativeKind.OriginalHardwareReference, [], 1),
        new("Cz-REF", "Cz", MontageNegativeKind.OriginalHardwareReference, [], 2),
        new("M1-REF", "M1", MontageNegativeKind.OriginalHardwareReference, [], 3),
        new("M2-REF", "M2", MontageNegativeKind.OriginalHardwareReference, [], 4),
    ];

    private static ChannelConfigurationProfile CreateChannelConfiguration() => new(
        "channels-1", "通道配置", "", ChannelConfigurationSource.User, "ant-eego|ref-inputs",
        [
            new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "F3", true, 0),
            new ChannelConfigurationEntry(1, AcquisitionChannelKind.Reference, "F4", true, 1),
            new ChannelConfigurationEntry(2, AcquisitionChannelKind.Reference, "Cz", true, 2),
            new ChannelConfigurationEntry(3, AcquisitionChannelKind.Reference, "M1", true, 3),
            new ChannelConfigurationEntry(4, AcquisitionChannelKind.Reference, "M2", true, 4),
        ],
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        HardwareReferenceElectrodeLocation: "FCz",
        HardwareGroundElectrodeLocation: "AFz");

    private static ChannelConfigurationProfile CreateAntSystemChannelConfiguration() => new(
        "system-ant-standard-28-input", "ANT默认通道配置", "", ChannelConfigurationSource.System, "ant-eego|28",
        [
            new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "Fp1", true, 0),
            new ChannelConfigurationEntry(1, AcquisitionChannelKind.Reference, "Fp2", true, 1),
            new ChannelConfigurationEntry(2, AcquisitionChannelKind.Reference, "F7", true, 2),
            new ChannelConfigurationEntry(3, AcquisitionChannelKind.Reference, "F3", true, 3),
            new ChannelConfigurationEntry(4, AcquisitionChannelKind.Reference, "Fz", true, 4),
            new ChannelConfigurationEntry(5, AcquisitionChannelKind.Reference, "F4", true, 5),
            new ChannelConfigurationEntry(6, AcquisitionChannelKind.Reference, "F8", true, 6),
            new ChannelConfigurationEntry(7, AcquisitionChannelKind.Reference, "T3", true, 7),
            new ChannelConfigurationEntry(8, AcquisitionChannelKind.Reference, "C3", true, 8),
            new ChannelConfigurationEntry(9, AcquisitionChannelKind.Reference, "Cz", true, 9),
            new ChannelConfigurationEntry(10, AcquisitionChannelKind.Reference, "C4", true, 10),
            new ChannelConfigurationEntry(11, AcquisitionChannelKind.Reference, "T4", true, 11),
            new ChannelConfigurationEntry(12, AcquisitionChannelKind.Reference, "M1", true, 12),
            new ChannelConfigurationEntry(13, AcquisitionChannelKind.Reference, "T5", true, 13),
            new ChannelConfigurationEntry(14, AcquisitionChannelKind.Reference, "P3", true, 14),
            new ChannelConfigurationEntry(15, AcquisitionChannelKind.Reference, "Pz", true, 15),
            new ChannelConfigurationEntry(16, AcquisitionChannelKind.Reference, "P4", true, 16),
            new ChannelConfigurationEntry(17, AcquisitionChannelKind.Reference, "T6", true, 17),
            new ChannelConfigurationEntry(18, AcquisitionChannelKind.Reference, "M2", true, 18),
            new ChannelConfigurationEntry(19, AcquisitionChannelKind.Reference, "O1", true, 19),
            new ChannelConfigurationEntry(21, AcquisitionChannelKind.Reference, "O2", true, 21),
        ], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}
