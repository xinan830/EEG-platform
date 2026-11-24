namespace BrainPlatform.Desktop.Tests.Montages;

public sealed class TemporaryMontageEditorTests
{
    [Fact]
    public async Task FullSessionEditorSavesOrderReferencesColorsAndNameWithoutWritingLibrary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"online-montage-{Guid.NewGuid():N}", "profiles.json");
        var original = TemporaryMontageFixture.Create("F3", "F4", "Cz");
        var channels = new ChannelConfigurationWorkspaceViewModel(new ChannelMappingViewModel());
        var editor = new MontageConfigurationWorkspaceViewModel(channels, new MontageProfileStore(path));
        MontageProfile? saved = null;
        editor.SessionProfileSaved += (_, profile) => saved = profile;
        editor.BeginSessionProfile(original.ChannelConfigurationSnapshot);
        Assert.False(editor.CanSelectDraftChannelConfiguration);
        Assert.Equal(3, editor.DraftRows.Count);
        editor.DraftName = "我的在线导联";
        editor.ApplyAverageReferenceToAll();
        editor.DraftRows[0].Color = "#345678";
        editor.OutputOrder.MoveDownCommand.Execute(editor.DraftRows[0]);

        await editor.SaveDraftAsync();

        Assert.NotNull(saved);
        Assert.Equal("我的在线导联", saved.Name);
        Assert.Equal(["F4", "F3", "Cz"], saved.DerivedChannels.Select(row => row.PositiveLabel));
        Assert.Equal([0, 1, 2], saved.DerivedChannels.Select(row => row.DisplayOrder));
        Assert.All(saved.DerivedChannels, row => Assert.Equal(MontageNegativeKind.Mean, row.NegativeKind));
        Assert.Equal("#345678", saved.DerivedChannels[1].Color);
        Assert.False(File.Exists(path));
        Assert.Empty(editor.Profiles);
        Assert.Null(editor.SelectedProfile);
        TemporaryWaveformMontage.ValidateSources(saved, ["f3", "F4", "Cz"]);

        var existing = saved;
        editor.BeginSessionProfile(original.ChannelConfigurationSnapshot, existing);
        Assert.Equal(existing.Name, editor.DraftName);
        editor.DraftName = "未保存的改动";
        editor.CloseDraft();
        Assert.Equal("我的在线导联", existing.Name);
        Assert.Same(existing, saved);
    }

    [Fact]
    public async Task InvalidDraftDoesNotSaveOrPublish()
    {
        var editor = new MontageConfigurationWorkspaceViewModel(
            new ChannelConfigurationWorkspaceViewModel(new ChannelMappingViewModel()));
        editor.BeginSessionProfile(TemporaryMontageFixture.Create("F3", "F4").ChannelConfigurationSnapshot);
        var saves = 0;
        editor.SessionProfileSaved += (_, _) => saves++;
        editor.DraftName = " ";
        await Assert.ThrowsAsync<InvalidOperationException>(editor.SaveDraftAsync);
        editor.DraftName = "在线导联";
        editor.DraftRows[0].Color = "bad-color";
        await Assert.ThrowsAsync<InvalidOperationException>(editor.SaveDraftAsync);
        Assert.Equal(0, saves);
    }

    [Theory]
    [InlineData("F3")]
    [InlineData("F3", "F4", "f3")]
    public void MissingOrAmbiguousSourcesAreRejected(params string[] labels) =>
        Assert.Throws<InvalidOperationException>(() => TemporaryWaveformMontage.ValidateSources(
            TemporaryMontageFixture.Create("F3", "F4"), labels));
}
