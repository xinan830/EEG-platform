using System.Collections.ObjectModel;
using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Montages;

public sealed class MontageAppearanceTests
{
    [Fact]
    public void ReorderingPreservesColorsFormulasAndNativeInputs()
    {
        var workspace = Workspace();
        workspace.BeginNewProfile();
        var f3 = workspace.DraftRows[0];
        var f7 = workspace.DraftRows[1];
        f3.Color = "#112233";
        f7.Color = "#445566";
        var snapshot = workspace.DraftChannelConfiguration!;
        var fingerprint = ChannelConfigurationFingerprint.Create(snapshot);

        Assert.False(workspace.OutputOrder.MoveUpCommand.CanExecute(f3));
        Assert.True(workspace.OutputOrder.MoveUpCommand.CanExecute(f7));
        workspace.OutputOrder.MoveUpCommand.Execute(f7);

        Assert.Same(f7, workspace.DraftRows[0]);
        Assert.Equal("#445566", f7.Color);
        Assert.Equal("7", f7.DeviceInputNumber);
        Assert.Equal("F7-REF", f7.ToModel([]).FormulaText);
        Assert.Equal(new[] { 0, 1 }, workspace.DraftRows.Select(row => row.DisplayOrder));
        Assert.Equal(fingerprint, ChannelConfigurationFingerprint.Create(snapshot));
        MontageValidation.Validate(snapshot, workspace.DraftRows.Select(row => row.ToModel([])).ToArray());
        workspace.OutputOrder.MoveDownCommand.Execute(f7);
        Assert.Same(f3, workspace.DraftRows[0]);
        Assert.Equal("#112233", f3.Color);
    }

    [Fact]
    public async Task SaveReloadAndCopyPreserveAppearanceAndBoundChannelIdentity()
    {
        var store = new MontageProfileStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "montages.json"));
        var workspace = Workspace(store);
        workspace.BeginNewProfile();
        workspace.DraftName = "颜色排序测试";
        workspace.DraftRows[0].Color = "#abcdef";
        workspace.DraftRows[1].Color = "#123456";
        workspace.OutputOrder.MoveUpCommand.Execute(workspace.DraftRows[1]);
        await workspace.SaveDraftAsync();

        var saved = Assert.Single(await store.LoadAsync(CancellationToken.None));
        Assert.Equal("source", saved.ChannelConfigurationSnapshot.Id);
        Assert.Equal(new[] { "F7-REF", "F3-REF" }, saved.DerivedChannels.Select(row => row.Name));
        Assert.Equal(new[] { "#123456", "#ABCDEF" }, saved.DerivedChannels.Select(row => row.Color));
        workspace.SelectedProfile = saved;
        workspace.BeginEditSelected();
        Assert.Equal(new[] { "F7", "F3" }, workspace.DraftRows.Select(row => row.PositiveLabel));
        Assert.Equal(new[] { "#123456", "#ABCDEF" }, workspace.DraftRows.Select(row => row.Color));
        workspace.BeginCopySelected();
        Assert.Equal(saved.DerivedChannels.Select(row => row.Name), workspace.DraftRows.Select(row => row.Name));
        workspace.DraftRows[0].Color = "#654321";
        Assert.Equal("#123456", saved.DerivedChannels[0].Color);
    }

    [Fact]
    public void SystemPresetsCannotMoveButCopiedPresetCan()
    {
        var workspace = Workspace();
        workspace.SelectedProfile = Profile() with { Source = MontageProfileSource.System };
        workspace.BeginEditSelected();
        var first = workspace.DraftRows[0];
        Assert.False(workspace.OutputOrder.MoveDownCommand.CanExecute(first));
        workspace.OutputOrder.MoveDownCommand.Execute(first);
        Assert.Same(first, workspace.DraftRows[0]);
        workspace.BeginCopySelected();
        Assert.True(workspace.OutputOrder.MoveDownCommand.CanExecute(workspace.DraftRows[0]));
    }

    [Fact]
    public void LegacyJsonAndFingerprintRemainReadable()
    {
        var legacy = JsonSerializer.Deserialize<DerivedMontageChannel>(
            """{"Name":"F3-REF","PositiveLabel":"F3","NegativeKind":0,"NegativeLabels":[],"DisplayOrder":0}""")!;
        Assert.Null(legacy.Color);
        var canonical = "channels|0:F3-REF:F3:OriginalHardwareReference:|AVG:";
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        Assert.Equal(expected, MontageProfileFingerprint.Create("channels", [legacy]));
        Assert.NotEqual(expected, MontageProfileFingerprint.Create("channels", [legacy with { Color = "#123456" }]));
        Assert.Equal(MontageProfileFingerprint.Create("channels", [legacy with { Color = "#abcdef" }]),
            MontageProfileFingerprint.Create("channels", [legacy with { Color = "#ABCDEF" }]));
    }

    [Fact]
    public void HiddenOutputsDoNotChangeOtherOutputColors()
    {
        var profile = Profile();
        Assert.Equal(WaveformTraceColors.DefaultAt(1), Assert.Single(
            MontageOutputAppearance.ResolveColors(profile, ["F7-REF"])));
        Assert.Equal(new[] { "#334455", "#112233" }, MontageOutputAppearance.ResolveColors(
            profile with { DerivedChannels = [profile.DerivedChannels[0] with { Color = "#112233" },
                profile.DerivedChannels[1] with { Color = "#334455" }] }, ["F7-REF", "F3-REF"]));
    }

    [Theory]
    [InlineData(-1, "#123456")]
    [InlineData(0, "red")]
    [InlineData(0, "#FF123456")]
    public void InvalidAppearanceIsRejected(int order, string color)
    {
        var profile = Profile();
        Assert.Throws<InvalidOperationException>(() => MontageValidation.Validate(profile.ChannelConfigurationSnapshot,
            [profile.DerivedChannels[0] with { DisplayOrder = order, Color = color }, profile.DerivedChannels[1]]));
    }

    [Fact]
    public void DuplicateOrdersAreRejected()
    {
        var profile = Profile();
        Assert.Throws<InvalidOperationException>(() => MontageValidation.Validate(profile.ChannelConfigurationSnapshot,
            [profile.DerivedChannels[0], profile.DerivedChannels[1] with { DisplayOrder = 0 }]));
    }

    internal static ChannelConfigurationProfile Source() => new("source", "测试通道", "",
        ChannelConfigurationSource.User, "device",
        [new(3, AcquisitionChannelKind.Reference, "F3", true, 0),
            new(7, AcquisitionChannelKind.Reference, "F7", true, 1)],
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    internal static MontageProfile Profile()
    {
        var source = Source();
        return new("montage", "测试导联", "", MontageProfileSource.User, source.DeviceSignature,
            ChannelConfigurationFingerprint.Create(source), source,
            [new("F3-REF", "F3", MontageNegativeKind.OriginalHardwareReference, [], 0),
                new("F7-REF", "F7", MontageNegativeKind.OriginalHardwareReference, [], 1)],
            1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    }

    private static MontageConfigurationWorkspaceViewModel Workspace(MontageProfileStore? store = null)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var channels = new ChannelConfigurationWorkspaceViewModel(
            new ChannelMappingViewModel(new ChannelLabelMappingStore(Path.Combine(root, "labels.json"))),
            new ChannelConfigurationProfileStore(Path.Combine(root, "channels.json")));
        channels.Profiles.Add(Source());
        return new(channels, store ?? new MontageProfileStore(Path.Combine(root, "montages.json")));
    }
}
