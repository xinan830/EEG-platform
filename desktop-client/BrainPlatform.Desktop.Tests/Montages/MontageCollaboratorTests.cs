using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.Stores;
using BrainPlatform.Desktop.Modules.Channels.ViewModels;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Montages.Stores;
using BrainPlatform.Desktop.Modules.Montages.ViewModels;

namespace BrainPlatform.Desktop.Tests.Montages;

public sealed class MontageCollaboratorTests
{
    [Fact]
    public async Task RefreshAsync_PreservesEmptySelectionWhenFilterHidesAllProfiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var channelStore = new ChannelConfigurationProfileStore(Path.Combine(dir, "channels.json"));
            var channelWorkspace = new ChannelConfigurationWorkspaceViewModel(
                new ChannelMappingViewModel(),
                channelStore,
                montageStore: new MontageProfileStore(Path.Combine(dir, "montages.json")));
            var montageStore = new MontageProfileStore(Path.Combine(dir, "montages.json"));

            var now = DateTimeOffset.UtcNow;
            var snapshot = new ChannelConfigurationProfile(
                "ch-1", "User Channels", "", ChannelConfigurationSource.User, "sig-1",
                [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "Fp1", true, 0)],
                now, now);
            var fp = ChannelConfigurationFingerprint.Create(snapshot);
            var outputs = new[]
            {
                new DerivedMontageChannel("Fp1-REF", "Fp1", MontageNegativeKind.OriginalHardwareReference, [], 0),
            };
            var userMontage = new MontageProfile(
                "m-1", "User Montage", "", MontageProfileSource.User, "sig-1", fp, snapshot, outputs,
                1, now, now, MontageProfileFingerprint.Create(fp, outputs));
            await montageStore.SaveAsync(userMontage, CancellationToken.None);

            var workspace = new MontageConfigurationWorkspaceViewModel(channelWorkspace, montageStore);
            workspace.SourceFilter = "系统默认";
            await workspace.RefreshAsync();

            Assert.Single(workspace.Profiles);
            Assert.Empty(workspace.FilteredProfiles);
            Assert.Null(workspace.SelectedProfile);
            Assert.False(workspace.CanDeleteSelected);

            workspace.SourceFilter = "全部来源";
            var visible = Assert.Single(workspace.FilteredProfiles);
            workspace.SelectedProfile = visible;
            Assert.True(workspace.CanDeleteSelected);

            workspace.SourceFilter = "系统默认";
            await workspace.RefreshAsync();
            Assert.Empty(workspace.FilteredProfiles);
            Assert.Null(workspace.SelectedProfile);
            Assert.False(workspace.CanDeleteSelected);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DraftEditor_EnforcesReadOnlyAndSupportsSessionDraftWithoutPersisting()
    {
        var dir = Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var channelWorkspace = new ChannelConfigurationWorkspaceViewModel(
                new ChannelMappingViewModel(),
                new ChannelConfigurationProfileStore(Path.Combine(dir, "channels.json")),
                montageStore: new MontageProfileStore(Path.Combine(dir, "montages.json")));
            var montageStore = new MontageProfileStore(Path.Combine(dir, "montages.json"));
            var workspace = new MontageConfigurationWorkspaceViewModel(channelWorkspace, montageStore);

            var now = DateTimeOffset.UtcNow;
            var snapshot = new ChannelConfigurationProfile(
                "ch-1", "Ch", "", ChannelConfigurationSource.System, "sig-1",
                [
                    new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "Fp1", true, 0),
                    new ChannelConfigurationEntry(1, AcquisitionChannelKind.Reference, "Fp2", true, 1),
                ],
                now, now);

            workspace.BeginSessionProfile(snapshot);
            Assert.True(workspace.IsSessionDraft);
            Assert.True(workspace.CanEditDraft);
            Assert.Equal(2, workspace.DraftRows.Count);

            workspace.ApplyAverageReferenceToAll();
            Assert.All(workspace.DraftRows, row => Assert.Equal(MontageNegativeKind.Mean, row.NegativeKind));

            MontageProfile? emitted = null;
            workspace.SessionProfileSaved += (_, profile) => emitted = profile;
            workspace.DraftName = "Session Test";
            await workspace.SaveDraftAsync();

            Assert.NotNull(emitted);
            Assert.Equal("Session Test", emitted.Name);
            Assert.Empty(await montageStore.LoadAsync(CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
