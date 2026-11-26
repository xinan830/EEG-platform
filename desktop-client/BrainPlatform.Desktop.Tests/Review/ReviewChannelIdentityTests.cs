using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using Xunit;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class ReviewPreprocessingTests
{
    [Fact]
    public async Task ScientificComparisonAllowsBackendDeclaredIdentityExcludedDisplayRows()
    {
        using var vm = new BrainPlatform.Desktop.Modules.Review.Preprocessing.ReviewPreprocessingViewModel(
            _ => Task.FromResult<(string, string?, double)>(("rec-1", null, 60)),
            () => new IdentityPreviewClient(), _ => { }, () => (0, 3, 1, 50, 50),
            getDisplayedChannels: () => ["Fp1", "C4"]);
        vm.IsOverlayCompareEnabled = true;
        await vm.RefreshOverlayWindowAsync();
        Assert.True(vm.IsScientificComparisonReady);
        Assert.True(vm.CanDisplayScientificComparison(["Fp1", "C4"]));
        Assert.False(vm.CanDisplayScientificComparison(["unknown", "C4"]));
        Assert.Equal(BrainPlatform.Desktop.Modules.Review.Preprocessing.ChannelVisualPreprocessingState.Excluded,
            vm.ResolveChannelBadge("Fp1").State);
        vm.IsOverlayCompareEnabled = false;
    }

    private sealed class IdentityPreviewClient : FakePreprocessingClient
    {
        public override async Task<PreprocessingPreviewWindowResponse> PreviewPreprocessedWindowAsync(string id,
            PreprocessingPreviewWindowRequest request, CancellationToken token)
        {
            var response = await base.PreviewPreprocessedWindowAsync(id, request, token);
            var times = new Dictionary<string, IReadOnlyList<double>> { ["C4"] = response.Baseline.TraceTimesSeconds["C4"] };
            var traces = new Dictionary<string, IReadOnlyList<double?>> { ["C4"] = response.Baseline.TracesUv["C4"] };
            return response with
            {
                Baseline = response.Baseline with { Channels = ["C4"], TraceTimesSeconds = times, TracesUv = traces },
                Processed = response.Processed with { Channels = ["C4"], TraceTimesSeconds = times, TracesUv = traces,
                    ExcludedOutputChannels = ["Fp1"] },
            };
        }
    }

    [Fact]
    public async Task EegProxySelectionKeepsIdentity_AndUnsavedIdentityBlocksScientificWork()
    {
        var client = new IdentityClient();
        using var vm = CreateViewModel(client, out _);
        await vm.LoadReferenceChannelsAsync();
        var frontal = vm.ReferenceChannels.Single(row => row.Name == "Fp1");
        frontal.IsEog = true;
        Assert.Equal("eeg", frontal.Identity);
        Assert.False(vm.HasUnsavedChannelIdentities);
        var report = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], report.IcaFitFingerprint);
        frontal.Identity = "eog";
        Assert.Null(vm.IcaFitFingerprint);
        Assert.True(vm.AreScientificInputsBlocked);
        Assert.Throws<InvalidOperationException>(() => vm.BuildPayload(true));
        await Assert.ThrowsAsync<InvalidOperationException>(vm.InspectIcaForDialogAsync);
        vm.IsOverlayCompareEnabled = true;
        await vm.RefreshOverlayWindowAsync();
        Assert.Equal(0, client.PreviewRequests);
        await vm.LoadReferenceChannelsAsync();
        Assert.Equal("eog", vm.ReferenceChannels.Single(row => row.Name == "Fp1").Identity);
        await vm.SaveChannelIdentitiesAsync();
        Assert.Equal("source-hash", client.Saved!.SourceSha256);
        Assert.Equal("eog", client.Saved.Channels.Single(row => row.Name == "Fp1").Identity);
        Assert.False(vm.HasUnsavedChannelIdentities);
        await vm.LoadReferenceChannelsAsync();
        Assert.Equal("eog", vm.ReferenceChannels.Single(row => row.Name == "Fp1").Identity);
        vm.IsOverlayCompareEnabled = false;
    }

    [Fact]
    public async Task SaveFailureKeepsIdentityDirtyAndScientificWorkBlocked()
    {
        var client = new IdentityClient { FailSave = true };
        using var vm = CreateViewModel(client, out _);
        await vm.LoadReferenceChannelsAsync();
        vm.ReferenceChannels[0].Identity = "eog";
        await vm.SaveChannelIdentitiesAsync();
        Assert.True(vm.HasUnsavedChannelIdentities);
        Assert.True(vm.AreScientificInputsBlocked);
        Assert.Contains("保存失败", vm.ReferenceStatusText);
        Assert.Throws<InvalidOperationException>(() => vm.BuildAlgorithmConfigDictionary(true));
    }

    [Fact]
    public async Task EcgScoringRequiresDedicatedIdentity_AndEditingInvalidatesPendingIca()
    {
        var client = new IdentityClient { DelayIca = true };
        using var vm = CreateViewModel(client, out _);
        await vm.LoadReferenceChannelsAsync();
        vm.RemovalMethod = "ica_infomax";
        vm.SelectedEcgChannel = "Fp1";
        Assert.True(vm.AreScientificInputsBlocked);
        Assert.Throws<InvalidOperationException>(() => vm.BuildPayload(true));
        vm.SelectedEcgChannel = "ECG1";
        Assert.False(vm.AreScientificInputsBlocked);
        var pending = vm.InspectIcaForDialogAsync();
        await client.Started.Task;
        vm.ReferenceChannels[0].Identity = "eog";
        client.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Null(vm.IcaFitFingerprint);
        Assert.Empty(vm.IcaComponents);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("ssp_eog")]
    [InlineData("eog_regression")]
    public async Task InactiveEcgIdentityDoesNotBlockOtherMethods_ButIcaFitStillValidates(string method)
    {
        var client = new IdentityClient();
        using var vm = CreateViewModel(client, out _);
        await vm.LoadReferenceChannelsAsync();
        vm.RemovalMethod = "ica_infomax";
        vm.SelectedEcgChannel = "Fp1";
        Assert.True(vm.AreScientificInputsBlocked);
        vm.IsOverlayCompareEnabled = true;
        vm.RemovalMethod = method;
        Assert.False(vm.AreScientificInputsBlocked);
        Assert.DoesNotContain("必须选择专用 ECG", vm.ReferenceStatusText);
        Assert.Empty(vm.BuildPayload(true).ArtifactRemoval.EcgChannels!);
        await vm.RefreshOverlayWindowAsync();
        Assert.NotNull(vm.CurrentOverlayWindow);
        await Assert.ThrowsAsync<InvalidOperationException>(vm.InspectIcaForDialogAsync);
        Assert.False(client.Started.Task.IsCompleted);
        vm.RemovalMethod = "ica_infomax";
        Assert.True(vm.AreScientificInputsBlocked);
        Assert.Null(vm.CurrentOverlayWindow);
        Assert.Contains("必须选择专用 ECG", vm.OverlayStatusText);
        vm.SelectedEcgChannel = "ECG1";
        Assert.False(vm.AreScientificInputsBlocked);
        vm.IsOverlayCompareEnabled = false;
    }

    [Fact]
    public async Task UnsavedIdentityUsesSameBlockingReasonImmediatelyAndAfterPreviewRefresh()
    {
        using var vm = CreateViewModel(new IdentityClient(), out _);
        await vm.LoadReferenceChannelsAsync();
        vm.IsOverlayCompareEnabled = true;
        await vm.RefreshOverlayWindowAsync();
        vm.ReferenceChannels.Single(row => row.Name == "Fp1").Identity = "eog";
        Assert.False(vm.HasReferenceSelectionConflict);
        Assert.Null(vm.CurrentOverlayWindow);
        var error = Assert.Throws<InvalidOperationException>(() => vm.BuildPayload(true));
        Assert.Contains("尚未保存", error.Message);
        Assert.Equal(error.Message, vm.OverlayStatusText);
        Assert.Equal(error.Message, vm.ReferenceStatusText);
        await vm.RefreshOverlayWindowAsync();
        Assert.Equal(error.Message, vm.OverlayStatusText);
        await vm.SaveChannelIdentitiesAsync();
        Assert.False(vm.AreScientificInputsBlocked);
        await vm.RefreshOverlayWindowAsync();
        Assert.NotNull(vm.CurrentOverlayWindow);
        vm.IsOverlayCompareEnabled = false;
    }

    [Fact]
    public async Task SwitchingMethodDiscardsPendingIcaWithoutBlockingTheNewMethod()
    {
        var client = new IdentityClient { DelayIca = true };
        using var vm = CreateViewModel(client, out _);
        await vm.LoadReferenceChannelsAsync();
        vm.RemovalMethod = "ica_infomax";
        vm.SelectedEcgChannel = "ECG1";
        var pending = vm.InspectIcaForDialogAsync();
        await client.Started.Task;
        try
        {
            vm.RemovalMethod = "none";
            Assert.False(vm.AreScientificInputsBlocked);
            Assert.Equal("none", vm.BuildPayload(true).ArtifactRemoval.Method);
        }
        finally { client.Release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(vm.IcaComponents);
        Assert.Null(vm.IcaFitFingerprint);
    }

    [Fact]
    public async Task MultipleDedicatedEcgRequiresSelectionOnlyForIca()
    {
        var client = new IdentityClient { MultipleEcg = true };
        using var vm = CreateViewModel(client, out _);
        await vm.LoadReferenceChannelsAsync();
        Assert.False(vm.AreScientificInputsBlocked);
        await Assert.ThrowsAsync<InvalidOperationException>(vm.InspectIcaForDialogAsync);
        Assert.False(client.Started.Task.IsCompleted);
        vm.RemovalMethod = "ica_infomax";
        Assert.True(vm.AreScientificInputsBlocked);
        Assert.Contains("指定一路", vm.ReferenceStatusText);
        vm.SelectedEcgChannel = "ECG2";
        Assert.False(vm.AreScientificInputsBlocked);
        var report = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], report.IcaFitFingerprint);
        Assert.Equal(new[] { "ECG2" }, vm.BuildPayload(true).ArtifactRemoval.EcgChannels);
        vm.SelectedEcgChannel = "";
        Assert.Null(vm.IcaFitFingerprint);
        Assert.True(vm.AreScientificInputsBlocked);
        vm.RemovalMethod = "none";
        Assert.False(vm.AreScientificInputsBlocked);
        vm.ReferenceChannels[0].Identity = "eog";
        Assert.True(vm.AreScientificInputsBlocked);
    }

    private sealed class IdentityClient : FakePreprocessingClient, BrainPlatform.Desktop.Modules.Algorithms.Api.IPreprocessingClient
    {
        private ArtifactReferenceCatalogue catalogue = new("rec-1", 500, 30000,
            [new("Fp1", "V", "eeg"), new("EOG1", "V", "eog"), new("ECG1", "V", "ecg")], "source-hash");
        public bool FailSave { get; init; }
        public bool MultipleEcg { get; init; }
        public bool DelayIca { get; init; }
        public int PreviewRequests { get; private set; }
        public ChannelIdentityUpdate? Saved { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<ArtifactReferenceCatalogue> GetArtifactReferenceChannelsAsync(string id, CancellationToken token) =>
            Task.FromResult(MultipleEcg ? catalogue with { Channels = [.. catalogue.Channels, new("ECG2", "V", "ecg")] } : catalogue);
        public Task<ArtifactReferenceCatalogue> SaveChannelIdentitiesAsync(string id, ChannelIdentityUpdate update, CancellationToken token = default)
        {
            if (FailSave) throw new IOException("disk full");
            Saved = update;
            catalogue = catalogue with { Channels = catalogue.Channels.Select(row => row with
                { Identity = update.Channels.Single(ch => ch.Name == row.Name).Identity }).ToArray() };
            return Task.FromResult(catalogue);
        }
        public override Task<PreprocessingPreviewWindowResponse> PreviewPreprocessedWindowAsync(string id, PreprocessingPreviewWindowRequest request, CancellationToken token)
        { PreviewRequests++; return base.PreviewPreprocessedWindowAsync(id, request, token); }
        public override async Task<IcaInspectionResponse> InspectIcaComponentsAsync(string id, string? montageId,
            double lowHz, double highHz, double? notchHz, IReadOnlyList<string>? badChannels, string badPolicy, string artifactPolicy,
            double? peakToPeakUv, double? muscleThreshold, bool allowProxy, int? components, double threshold,
            IReadOnlyList<string>? eog, IReadOnlyList<string>? ecg, CancellationToken token)
        {
            Started.TrySetResult();
            if (DelayIca) await Release.Task;
            return await base.InspectIcaComponentsAsync(id, montageId, lowHz, highHz, notchHz, badChannels, badPolicy, artifactPolicy,
                peakToPeakUv, muscleThreshold, allowProxy, components, threshold, eog, ecg, token);
        }
    }
}
