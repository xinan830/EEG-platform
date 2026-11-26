using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;
using Xunit;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class ReviewPreprocessingTests
{
    [Fact]
    public async Task ArtifactScan_UsesEffectiveReferencesAndInvalidatesWhenTheirPopulationChanges()
    {
        var client = new ReferencePolicyClient();
        using var vm = CreateViewModel(client, out _);
        await vm.LoadReferenceChannelsAsync();
        vm.ReferenceChannels.Single(c => c.Name == "EOG1").IsEog = true;
        vm.SelectedEcgChannel = "ECG1";
        vm.RemovalMethod = "ica_infomax";
        await vm.InspectArtifactSegmentsAsync();
        Assert.Empty(client.ScanEog!);
        Assert.Empty(client.ScanEcg!);
        Assert.NotEmpty(vm.ArtifactMarkers);
        var report = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], report.IcaFitFingerprint);
        Assert.Empty(vm.ArtifactMarkers);
        await vm.InspectArtifactSegmentsAsync();
        Assert.Equal(new[] { "EOG1" }, client.ScanEog);
        Assert.Equal(new[] { "ECG1" }, client.ScanEcg);
        Assert.Equal(report.IcaFitFingerprint, vm.IcaFitFingerprint);
        Assert.NotEmpty(vm.ArtifactMarkers);
        vm.RemovalMethod = "none";
        Assert.Empty(vm.ArtifactSegments);
        Assert.Empty(vm.ArtifactMarkers);
        await vm.InspectArtifactSegmentsAsync();
        Assert.Empty(client.ScanEog!);
        Assert.Empty(client.ScanEcg!);
        vm.RemovalMethod = "ica_infomax";
        await vm.InspectArtifactSegmentsAsync();
        vm.IcaComponentCount++;
        Assert.Null(vm.IcaFitFingerprint);
        Assert.Empty(vm.ArtifactMarkers);
    }

    [Fact]
    public async Task FirstScan_AutoEnablingRejectionDoesNotPublishReportForPreviousReferencePopulation()
    {
        var client = new ReferencePolicyClient();
        using var vm = CreateViewModel(client, out _);
        await vm.LoadReferenceChannelsAsync();
        vm.ReferenceChannels.Single(c => c.Name == "EOG1").IsEog = true;
        var report = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], report.IcaFitFingerprint);
        await vm.InspectArtifactSegmentsAsync();
        Assert.Equal("reject", vm.ArtifactPolicy);
        Assert.Null(vm.IcaFitFingerprint);
        Assert.Empty(vm.ArtifactSegments);
        Assert.Empty(vm.ArtifactMarkers);
        Assert.Contains("重新扫描", vm.StatusMessage);
        await vm.InspectArtifactSegmentsAsync();
        Assert.Empty(client.ScanEog!);
        Assert.NotEmpty(vm.ArtifactMarkers);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("eog_regression")]
    [InlineData("ssp_eog")]
    [InlineData("ica_infomax")]
    public async Task OverlappingRoles_BlockAllScientificRequestsAndRecoverAfterReselection(string method)
    {
        var client = new ReferencePolicyClient();
        using var vm = CreateViewModel(client, out _);
        await vm.LoadReferenceChannelsAsync();
        vm.RemovalMethod = method;
        vm.ReferenceChannels.Single(c => c.Name == "EOG1").IsEog = true;
        vm.SelectedEcgChannel = "EOG1";
        Assert.True(vm.HasReferenceSelectionConflict);
        Assert.Throws<InvalidOperationException>(() => vm.BuildPayload(true));
        Assert.Throws<InvalidOperationException>(() => vm.BuildAlgorithmConfigDictionary(false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.InspectArtifactSegmentsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.InspectIcaForDialogAsync());
        vm.IsOverlayCompareEnabled = true;
        await vm.RefreshOverlayWindowAsync();
        Assert.Equal(0, client.PreviewRequests);
        Assert.Null(client.ScanEog);
        Assert.Null(vm.CurrentOverlayWindow);
        Assert.Contains("冲突", vm.OverlayStatusText);
        await vm.LoadReferenceChannelsAsync();
        Assert.Contains("不能同时", vm.ReferenceStatusText);
        vm.SelectedEcgChannel = "ECG1";
        Assert.False(vm.HasReferenceSelectionConflict);
        await vm.RefreshOverlayWindowAsync();
        Assert.NotNull(vm.CurrentOverlayWindow);
        Assert.True(client.PreviewRequests > 0);
    }

    [Fact]
    public async Task ReferenceConflict_DiscardsPreviewAlreadyInFlight()
    {
        var client = new ControlledClient { DelayPreview = true };
        using var vm = CreateViewModel(client, out _);
        await vm.LoadReferenceChannelsAsync();
        vm.ReferenceChannels.Single(c => c.Name == "EOG1").IsEog = true;
        vm.RemovalMethod = "eog_regression";
        vm.IsOverlayCompareEnabled = true;
        var pending = vm.RefreshOverlayWindowAsync();
        await client.PreviewStarted.Task;
        vm.SelectedEcgChannel = "EOG1";
        client.PreviewRelease.TrySetResult();
        await pending;
        Assert.Null(vm.CurrentOverlayWindow);
        Assert.Contains("冲突", vm.OverlayStatusText);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("interpolate")]
    [InlineData("exclude")]
    public async Task ReferenceExclusionBadge_UsesBackendOutputsRegardlessOfBadChannelPolicy(string policy)
    {
        var client = new ReferencePolicyClient();
        using var vm = CreateViewModel(client, out _);
        vm.BadChannelPolicy = policy;
        vm.IsOverlayCompareEnabled = true;
        await vm.RefreshOverlayWindowAsync();
        var badge = vm.ResolveChannelBadge("Fp1");
        Assert.Equal(ChannelVisualPreprocessingState.Excluded, badge.State);
        Assert.Contains("眼电/心电", badge.Tooltip);
        Assert.Equal(policy == "interpolate" ? ChannelVisualPreprocessingState.Interpolated : ChannelVisualPreprocessingState.Normal,
            vm.ResolveChannelBadge("C4").State);
        vm.IsOverlayCompareEnabled = false;
        Assert.Equal(ChannelVisualPreprocessingState.Normal, vm.ResolveChannelBadge("Fp1").State);
    }

    private sealed class ReferencePolicyClient : FakePreprocessingClient
    {
        public IReadOnlyList<string>? ScanEog { get; private set; }
        public IReadOnlyList<string>? ScanEcg { get; private set; }
        public int PreviewRequests { get; private set; }

        public override Task<ArtifactSegmentInspectionResponse> InspectArtifactSegmentsAsync(
            string recordingId, double startSeconds, double windowSeconds, string? montageId,
            double? peakToPeakUv, double? muscleZScoreThreshold, IReadOnlyList<string>? badChannels,
            string badChannelPolicy, IReadOnlyList<string>? eogChannels,
            IReadOnlyList<string>? ecgChannels, CancellationToken cancellationToken)
        {
            ScanEog = eogChannels;
            ScanEcg = ecgChannels;
            return base.InspectArtifactSegmentsAsync(recordingId, startSeconds, windowSeconds, montageId,
                peakToPeakUv, muscleZScoreThreshold, badChannels, badChannelPolicy, eogChannels, ecgChannels, cancellationToken);
        }

        public override async Task<PreprocessingPreviewWindowResponse> PreviewPreprocessedWindowAsync(
            string recordingId, PreprocessingPreviewWindowRequest request, CancellationToken cancellationToken)
        {
            PreviewRequests++;
            var preview = await base.PreviewPreprocessedWindowAsync(recordingId, request, cancellationToken);
            return preview with { Processed = preview.Processed with { ExcludedOutputChannels = ["Fp1"] } };
        }
    }
}
