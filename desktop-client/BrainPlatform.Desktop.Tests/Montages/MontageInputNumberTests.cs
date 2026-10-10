namespace BrainPlatform.Desktop.Tests.Montages;

public sealed class MontageInputNumberTests
{
    private static ChannelConfigurationEntry[] Sources =>
    [
        new(3, AcquisitionChannelKind.Reference, "F88", true, 1),
        new(5, AcquisitionChannelKind.Reference, "F3", true, 0),
        new(9, AcquisitionChannelKind.Reference, "Cz", false, 2),
    ];

    private static MontageDraftRow CreateRow(MontageNegativeKind kind,
        ChannelConfigurationEntry[]? sources = null) => new(
        "F88", 1, ["F88", "F3", "Cz"], kind, "F3", "F3", "Cz",
        "平均参考", "双参考", sources ?? Sources, ["F88", "F3"]);

    [Fact]
    public void NumbersUseNativeInputsNotDisplayOrder_AndDoNotChangeFormula()
    {
        var row = CreateRow(MontageNegativeKind.Channel);
        var before = row.ToModel([]);
        Assert.Equal("3", row.DeviceInputNumber);
        Assert.Equal("5", row.ReferenceInputNumbers);
        var after = row.ToModel([]);
        Assert.Equal(before.FormulaText, after.FormulaText);
        Assert.Equal(before.PositiveLabel, after.PositiveLabel);
        Assert.Equal(before.NegativeKind, after.NegativeKind);
        Assert.Equal(before.NegativeLabels.ToArray(), after.NegativeLabels.ToArray());
        Assert.Equal(before.DisplayOrder, after.DisplayOrder);
        Assert.Equal("F88", row.PositiveLabel);
    }

    [Fact]
    public void SwappedMappingChangesNumbers_NotLabels()
    {
        var row = CreateRow(MontageNegativeKind.Channel,
            Sources.Select(source => source with
            {
                NativeChannelIndex = source.NativeChannelIndex == 3 ? 5
                    : source.NativeChannelIndex == 5 ? 3 : 9,
            }).ToArray());
        Assert.Equal("5", row.DeviceInputNumber);
        Assert.Equal("3", row.ReferenceInputNumbers);
    }

    [Fact]
    public void HardwareReferenceHasNoSampledInput()
    {
        Assert.Equal("—", CreateRow(MontageNegativeKind.OriginalHardwareReference).ReferenceInputNumbers);
    }

    [Fact]
    public void ChannelAndPairChangesNotifyReferenceNumbers()
    {
        var row = CreateRow(MontageNegativeKind.Channel);
        var changes = new List<string?>();
        row.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        row.SelectedNegativeLabel = "Cz";
        Assert.Equal("9", row.ReferenceInputNumbers);
        Assert.Contains(nameof(MontageDraftRow.ReferenceInputNumbers), changes);
        row.NegativeKind = MontageNegativeKind.SpecifiedPair;
        Assert.Equal("5、9", row.ReferenceInputNumbers);
        changes.Clear();
        row.SelectedPairFirstLabel = "F88";
        Assert.Equal("3、9", row.ReferenceInputNumbers);
        Assert.Contains(nameof(MontageDraftRow.ReferenceInputNumbers), changes);
    }

    [Fact]
    public void AverageGroupUsesActualMembership_NotSummaryText()
    {
        var row = CreateRow(MontageNegativeKind.Mean);
        Assert.Equal("3、5", row.ReferenceInputNumbers);
        var notified = false;
        row.PropertyChanged += (_, args) => notified |= args.PropertyName == nameof(MontageDraftRow.ReferenceInputNumbers);
        row.UpdateReferenceSummaries("任意展示文本", "", ["Cz", "F3"]);
        Assert.Equal("9、5", row.ReferenceInputNumbers);
        Assert.True(notified);
    }

    [Fact]
    public void MissingAmbiguousAndInvalidSourcesAreNotGuessed()
    {
        Assert.Equal("未知", CreateRow(MontageNegativeKind.Channel, []).DeviceInputNumber);
        Assert.Equal("未知", CreateRow(MontageNegativeKind.Channel,
            [.. Sources, new(7, AcquisitionChannelKind.Reference, "f88", true, 3)]).DeviceInputNumber);
        Assert.Equal("未知", CreateRow(MontageNegativeKind.Channel,
            [Sources[0] with { NativeChannelIndex = -1 }]).DeviceInputNumber);
        Assert.Equal("未知", CreateRow(MontageNegativeKind.Channel,
            [.. Sources, new(3, AcquisitionChannelKind.Reference, "Other", true, 3)]).DeviceInputNumber);
    }

    [Fact]
    public void PresentationCopiesSnapshotCollection()
    {
        var sources = Sources;
        var row = CreateRow(MontageNegativeKind.Channel, sources);
        sources[0] = sources[0] with { NativeChannelIndex = 17 };
        Assert.Equal("3", row.DeviceInputNumber);
    }

    [Theory]
    [InlineData(MontageProfileSource.User)]
    [InlineData(MontageProfileSource.System)]
    public void WorkspaceDetailsUseSavedSnapshot_NotCurrentPreset(MontageProfileSource kind)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var snapshot = new ChannelConfigurationProfile("source", "Saved", "",
            ChannelConfigurationSource.User, "test-device", Sources,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var channelWorkspace = new ChannelConfigurationWorkspaceViewModel(
            new ChannelMappingViewModel(new ChannelLabelMappingStore(Path.Combine(tempRoot, "labels.json"))),
            new ChannelConfigurationProfileStore(Path.Combine(tempRoot, "channels.json")));
        channelWorkspace.Profiles.Add(snapshot with
        {
            Channels = Sources.Select(source => source with { NativeChannelIndex = source.NativeChannelIndex + 20 }).ToArray(),
        });
        var workspace = new MontageConfigurationWorkspaceViewModel(channelWorkspace,
            new MontageProfileStore(Path.Combine(tempRoot, "montages.json")));
        workspace.SelectedProfile = new MontageProfile("montage", "Saved montage", "", kind,
            snapshot.DeviceSignature, ChannelConfigurationFingerprint.Create(snapshot), snapshot,
            [new("F88-F3", "F88", MontageNegativeKind.Channel, ["F3"], 1)],
            1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        workspace.BeginEditSelected();

        var row = Assert.Single(workspace.DraftRows);
        Assert.Equal("3", row.DeviceInputNumber);
        Assert.Equal("5", row.ReferenceInputNumbers);
    }

    [Fact]
    public void WorkspaceNewRowsAndAverageGroupRemainSynchronized()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var channelWorkspace = new ChannelConfigurationWorkspaceViewModel(
            new ChannelMappingViewModel(new ChannelLabelMappingStore(Path.Combine(tempRoot, "labels.json"))),
            new ChannelConfigurationProfileStore(Path.Combine(tempRoot, "channels.json")));
        channelWorkspace.Profiles.Add(new ChannelConfigurationProfile("source", "Test", "",
            ChannelConfigurationSource.User, "test-device", Sources,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        var workspace = new MontageConfigurationWorkspaceViewModel(channelWorkspace,
            new MontageProfileStore(Path.Combine(tempRoot, "montages.json")));
        workspace.BeginNewProfile();
        Assert.Equal(new[] { "5", "3" }, workspace.DraftRows.Select(row => row.DeviceInputNumber));
        workspace.ApplyAverageReferenceToAll();
        var row = workspace.DraftRows[0];
        Assert.Equal("5、3", row.ReferenceInputNumbers);
        workspace.AverageReferenceChannels.Single(option => option.Label == "F88").IsIncluded = false;
        Assert.Equal("5", row.ReferenceInputNumbers);
    }
}
