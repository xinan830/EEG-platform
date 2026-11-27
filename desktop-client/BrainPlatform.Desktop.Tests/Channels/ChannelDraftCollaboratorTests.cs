using BrainPlatform.Desktop.Modules.Channels.Api;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.Services;
using BrainPlatform.Desktop.Modules.Channels.ViewModels;

namespace BrainPlatform.Desktop.Tests.Channels;

public sealed class ChannelDraftCollaboratorTests
{
    [Fact]
    public void DraftEditorClearsRowSubscriptionsAndTracksBaselineAcrossEditCopyNewVersion()
    {
        var mapping = new ChannelMappingViewModel();
        var capabilities = new[]
        {
            new AcquisitionChannelCapability(0, AcquisitionChannelKind.Reference, "V"),
            new AcquisitionChannelCapability(1, AcquisitionChannelKind.Reference, "V"),
        };
        var device = new AcquisitionDeviceDescriptor(
            "dev-1", "Test Device", null, [], ChannelCapabilities: capabilities, Model: "M1", DriverId: "mock");
        var sig = AcquisitionDeviceCompatibility.CreateSignature("mock", capabilities);
        mapping.LoadDevice(device);

        var saved = new ChannelConfigurationProfile(
            "u-1", "User 1", "Desc", ChannelConfigurationSource.User, sig,
            [
                new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "Fp1", true, 0),
                new ChannelConfigurationEntry(1, AcquisitionChannelKind.Reference, "Fp2", true, 1),
            ],
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Test Device", "mock", "M1");

        var editor = new ChannelConfigurationDraftEditor(mapping, deviceSession: null, notifySuccess: null);
        editor.BeginNew(showDevicePicker: true);
        Assert.True(editor.IsDraftOpen);
        editor.DraftDevice = device;
        Assert.Equal(2, editor.DraftRows.Count);
        var oldRow = editor.DraftRows[0];

        editor.BeginEdit(saved);
        Assert.False(editor.HasUnsavedChanges);
        var changes = new List<string?>();
        editor.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        oldRow.IsEnabled = false; // Unsubscribed row must not notify editor.
        Assert.DoesNotContain(nameof(ChannelConfigurationDraftEditor.HasUnsavedChanges), changes);

        editor.DraftRows[0].IsEnabled = false;
        Assert.True(editor.HasUnsavedChanges);

        editor.BeginCopy(saved);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Contains("副本", editor.DraftName, StringComparison.Ordinal);

        editor.BeginNewVersion(saved);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Contains("新版本", editor.DraftName, StringComparison.Ordinal);

        editor.Close();
        Assert.False(editor.IsDraftOpen);
        Assert.Empty(editor.DraftRows);
    }

    [Fact]
    public async Task LocationResolverSkipsRowsWhoseLabelChangedDuringAsyncResolve()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new DeferredElectrodeLocationClient(gate);
        var resolver = new ChannelDraftLocationResolver(client);
        var row0 = new ChannelConfigurationDraftRow(0, "Reference", "V", "µV", "Fp1", true, 0, true);
        var row1 = new ChannelConfigurationDraftRow(1, "Reference", "V", "µV", "Fp2", true, 1, true);

        var task = resolver.ResolveAsync([row0, row1], CancellationToken.None);
        row0.ElectrodeLabel = "Fz"; // Mutated while resolve is in flight.
        gate.SetResult();
        await task;

        Assert.Null(row0.ElectrodeLocation);
        Assert.NotNull(row1.ElectrodeLocation);
        Assert.Equal("Fp2", row1.ElectrodeLocation!.CanonicalSite);
    }

    [Fact]
    public void ProfileCatalogueFiltersAndPaginatesConsistently()
    {
        var loaded = false;
        var catalogue = new ChannelProfileCatalogueView(() => loaded) { PageSize = 10 };
        var profiles = Enumerable.Range(1, 15).Select(i => new ChannelConfigurationProfile(
            $"id-{i}", $"Config {i:00}", "", i == 1 ? ChannelConfigurationSource.System : ChannelConfigurationSource.User,
            "sig", [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Dev", "mock", $"Model{i % 2}")).ToArray();

        catalogue.ReplaceProfiles(profiles);
        Assert.Equal(2, catalogue.TotalPages);
        Assert.Equal(10, catalogue.FilteredProfiles.Count);
        catalogue.CurrentPage = 99;
        Assert.Equal(2, catalogue.CurrentPage);
        Assert.Equal(5, catalogue.FilteredProfiles.Count);

        catalogue.SourceFilter = "系统预设";
        Assert.Equal(1, catalogue.TotalItemCount);
        Assert.Equal("id-1", catalogue.FilteredProfiles[0].Id);
    }

    [Fact]
    public async Task RefreshAsync_PreservesEmptySelectionWhenFilterHidesAllProfiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new ChannelConfigurationProfileStore(Path.Combine(dir, "channels.json"));
            var now = DateTimeOffset.UtcNow;
            var userProfile = new ChannelConfigurationProfile(
                "user-1", "User Config", "", ChannelConfigurationSource.User,
                "sig-1", [], now, now, "Dev", "mock", "ModelA");
            await store.SaveAsync(userProfile, CancellationToken.None);

            var workspace = new ChannelConfigurationWorkspaceViewModel(
                new ChannelMappingViewModel(),
                store,
                montageStore: new MontageProfileStore(Path.Combine(dir, "montages.json")));

            workspace.SourceFilter = "系统预设";
            await workspace.RefreshAsync();

            Assert.Single(workspace.Profiles);
            Assert.Empty(workspace.FilteredProfiles);
            Assert.Null(workspace.SelectedProfile);
            Assert.False(workspace.CanDeleteSelected);

            workspace.SourceFilter = "全部来源";
            var visible = Assert.Single(workspace.FilteredProfiles);
            Assert.Equal("User Config", visible.Name);

            // Re-filtering to hide the previously selected user profile and refreshing must keep selection empty.
            workspace.SelectedProfile = visible;
            Assert.True(workspace.CanDeleteSelected);
            workspace.SourceFilter = "系统预设";
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

    private sealed class DeferredElectrodeLocationClient(TaskCompletionSource gate) : IElectrodeLocationClient
    {
        public async Task<IReadOnlyList<ElectrodeLocationResolution>> ResolveAsync(
            IReadOnlyList<string> labels, CancellationToken cancellationToken)
        {
            await gate.Task.WaitAsync(cancellationToken);
            return labels.Select(label => new ElectrodeLocationResolution(
                label, "available", label, [0.01, 0.02, 0.03], [0.1, 0.2], "mri", "m", "colin27", "1")).ToArray();
        }

        public Task<ElectrodeScalpModel3D> GetScalpModel3DAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
