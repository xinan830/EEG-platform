using Xunit;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class ReviewPreprocessingTests
{
    [Fact]
    public async Task TemporaryMontage_DiscardsLatePreviewAndResumesWithoutInvalidatingIca()
    {
        var client = new ControlledClient { DelayPreview = true };
        using var vm = CreateViewModel(client, out _);
        var report = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], report.IcaFitFingerprint);
        var changes = 0;
        vm.ConfigurationChanged += (_, _) => changes++;
        vm.IsOverlayCompareEnabled = true;
        var pending = vm.RefreshOverlayWindowAsync();
        await client.PreviewStarted.Task;
        vm.SetDisplayMontageCompatible(false);
        client.PreviewRelease.SetResult();
        await pending;
        Assert.Null(vm.CurrentOverlayWindow);
        Assert.Contains("临时导联", vm.OverlayStatusText);
        vm.SetPlaybackActive(true);
        vm.SetDisplayMontageCompatible(true);
        await vm.RefreshOverlayWindowAsync();
        Assert.Null(vm.CurrentOverlayWindow);
        Assert.Contains("播放中", vm.OverlayStatusText);
        vm.SetPlaybackActive(false);
        await vm.RefreshOverlayWindowAsync();
        Assert.NotNull(vm.CurrentOverlayWindow);
        Assert.Equal(report.IcaFitFingerprint, vm.IcaFitFingerprint);
        Assert.Equal(new[] { 0 }, vm.ConfirmedIcaExcluded);
        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnchangedRescan_PreservesConfirmedIcaAndCurrentPreview(bool scanBadChannels)
    {
        using var vm = CreateViewModel(new FakePreprocessingClient(), out _);
        await vm.InspectBadChannelsAsync();
        await vm.InspectArtifactSegmentsAsync();
        var report = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], report.IcaFitFingerprint);
        vm.IsOverlayCompareEnabled = true;
        await vm.RefreshOverlayWindowAsync();
        var preview = vm.CurrentOverlayWindow;
        var summary = vm.IcaSummaryText;
        var changes = 0;
        vm.ConfigurationChanged += (_, _) => changes++;

        if (scanBadChannels) await vm.InspectBadChannelsAsync();
        else await vm.InspectArtifactSegmentsAsync();

        Assert.Equal(report.IcaFitFingerprint, vm.IcaFitFingerprint);
        Assert.Equal(new[] { 0 }, vm.ConfirmedIcaExcluded);
        Assert.Equal(summary, vm.IcaSummaryText);
        Assert.Equal(0, changes);
        Assert.Same(preview, vm.CurrentOverlayWindow);
    }

    [Fact]
    public async Task InterpolationBadge_UsesVerifiedNegativeAndAverageReferenceDependencies()
    {
        using var vm = CreateViewModel(new ReferencePreviewClient(), out _);
        await vm.InspectBadChannelsAsync();
        vm.IsOverlayCompareEnabled = true;
        await vm.RefreshOverlayWindowAsync();
        foreach (var output in new[] { "F4-C4", "F3-AVG" })
            Assert.Equal(BrainPlatform.Desktop.Modules.Review.Preprocessing.ChannelVisualPreprocessingState.Interpolated,
                vm.ResolveChannelBadge(output).State);
        vm.IsOverlayCompareEnabled = false;
        Assert.Equal(BrainPlatform.Desktop.Modules.Review.Preprocessing.ChannelVisualPreprocessingState.Normal,
            vm.ResolveChannelBadge("F4-C4").State);
    }

    [Fact]
    public async Task BadChannelRescan_DoesNotReenableExplicitlyDisabledPolicy()
    {
        using var vm = CreateViewModel(new FakePreprocessingClient(), out _);
        await vm.InspectBadChannelsAsync();
        vm.BadChannelPolicy = "none";
        var report = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], report.IcaFitFingerprint);
        await vm.InspectBadChannelsAsync();
        Assert.Equal("none", vm.BadChannelPolicy);
        Assert.Equal(report.IcaFitFingerprint, vm.IcaFitFingerprint);
    }

    private sealed class ReferencePreviewClient : FakePreprocessingClient
    {
        public override async Task<BrainPlatform.Desktop.Modules.Algorithms.Contracts.PreprocessingPreviewWindowResponse> PreviewPreprocessedWindowAsync(
            string recordingId, BrainPlatform.Desktop.Modules.Algorithms.Contracts.PreprocessingPreviewWindowRequest request, CancellationToken cancellationToken)
        {
            var preview = await base.PreviewPreprocessedWindowAsync(recordingId, request, cancellationToken);
            return preview with
            {
                Channels = ["F4-C4", "F3-AVG"], InterpolatedChannels = ["F4-C4", "F3-AVG"],
                TraceTimesSeconds = new Dictionary<string, IReadOnlyList<double>>
                { ["F4-C4"] = preview.TraceTimesSeconds["Fp1"], ["F3-AVG"] = preview.TraceTimesSeconds["C4"] },
                TracesUv = new Dictionary<string, IReadOnlyList<double?>>
                { ["F4-C4"] = preview.TracesUv["Fp1"], ["F3-AVG"] = preview.TracesUv["C4"] },
            };
        }
    }

    [Fact]
    public async Task FirstArtifactScan_EnablingRejectionInvalidatesConfirmedIca()
    {
        using var vm = CreateViewModel(new FakePreprocessingClient(), out _);
        var report = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], report.IcaFitFingerprint);
        await vm.InspectArtifactSegmentsAsync();
        Assert.Equal("reject", vm.ArtifactPolicy);
        Assert.Null(vm.IcaFitFingerprint);
        Assert.Contains("重新审查", vm.IcaSummaryText);
    }

    [Fact]
    public async Task UnchangedRescans_PreservePendingIcaForConfirmation()
    {
        using var vm = CreateViewModel(new FakePreprocessingClient(), out _);
        await vm.InspectBadChannelsAsync();
        await vm.InspectArtifactSegmentsAsync();
        var report = await vm.InspectIcaForDialogAsync();
        await vm.InspectBadChannelsAsync();
        await vm.InspectArtifactSegmentsAsync();
        vm.ConfirmIcaSelection([0], report.IcaFitFingerprint);
        Assert.Equal(report.IcaFitFingerprint, vm.IcaFitFingerprint);
    }

    [Fact]
    public async Task UnreviewedIca_KeepsInitialStatusWhenInputsChange()
    {
        using var vm = CreateViewModel(new FakePreprocessingClient(), out _);
        var initial = vm.IcaSummaryText;
        vm.PeakToPeakUv += 50;
        await vm.InspectBadChannelsAsync();
        await vm.InspectArtifactSegmentsAsync();
        Assert.Equal(initial, vm.IcaSummaryText);
    }

    [Fact]
    public async Task BadChannelRescan_PreservesManualSelectionsInsteadOfReapplyingRecommendations()
    {
        using var vm = CreateViewModel(new FakePreprocessingClient(), out _);
        await vm.InspectBadChannelsAsync();
        vm.BadChannelRows.Single(row => row.Channel == "C4").IsSelected = false;
        var report = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], report.IcaFitFingerprint);
        await vm.InspectBadChannelsAsync();
        Assert.Empty(vm.SelectedBadChannels);
        Assert.Equal(report.IcaFitFingerprint, vm.IcaFitFingerprint);
    }
}
