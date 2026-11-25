using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using Xunit;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class ReviewPreprocessingTests
{
    [Fact]
    public async Task CancelledSecondIcaReview_PreservesConfirmedModelAndComponents()
    {
        var client = new ControlledClient();
        using var vm = CreateViewModel(client, out _);
        var first = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], first.IcaFitFingerprint);
        client.NextFingerprint = "second-model";
        await vm.InspectIcaForDialogAsync();
        vm.CancelIcaReview();
        Assert.Equal(first.IcaFitFingerprint, vm.IcaFitFingerprint);
        Assert.Equal(new[] { 0 }, vm.ConfirmedIcaExcluded);
    }

    [Fact]
    public async Task ConfirmIca_RejectsUnknownComponentsAndUnreviewedFingerprint()
    {
        using var vm = CreateViewModel(new FakePreprocessingClient(), out _);
        var report = await vm.InspectIcaForDialogAsync();
        Assert.Throws<InvalidOperationException>(() => vm.ConfirmIcaSelection([99], report.IcaFitFingerprint));
        Assert.Throws<InvalidOperationException>(() => vm.ConfirmIcaSelection([0], "unreviewed"));
        Assert.Null(vm.IcaFitFingerprint);
    }

    [Fact]
    public async Task BadChannelRescan_InvalidatesConfirmedIca()
    {
        using var vm = CreateViewModel(new FakePreprocessingClient(), out _);
        var report = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], report.IcaFitFingerprint);
        await vm.InspectBadChannelsAsync();
        Assert.Null(vm.IcaFitFingerprint);
        Assert.Null(vm.ConfirmedIcaExcluded);
    }

    [Fact]
    public async Task Reset_DiscardsLateIcaInspection()
    {
        var client = new ControlledClient { DelayIca = true };
        using var vm = CreateViewModel(client, out _);
        var inspection = vm.InspectIcaForDialogAsync();
        await client.IcaStarted.Task;
        vm.ResetAll();
        client.IcaRelease.SetResult();
        try { await inspection; } catch (OperationCanceledException) { }
        Assert.Empty(vm.IcaComponents);
        Assert.Null(vm.IcaFitFingerprint);
        Assert.Contains("已重置", vm.StatusMessage);
    }

    [Fact]
    public async Task PreviewFailure_RemovesPreviousProcessedSamples()
    {
        var client = new ControlledClient();
        using var vm = CreateViewModel(client, out _);
        vm.IsOverlayCompareEnabled = true;
        await vm.RefreshOverlayWindowAsync();
        Assert.NotNull(vm.CurrentOverlayWindow);
        client.FailPreview = true;
        await vm.RefreshOverlayWindowAsync();
        Assert.Null(vm.CurrentOverlayWindow);
        Assert.Contains("失败", vm.OverlayStatusText);
    }

    [Fact]
    public async Task Reset_DiscardsPreviewEvenWhenTransportIgnoresCancellation()
    {
        var client = new ControlledClient { DelayPreview = true };
        using var vm = CreateViewModel(client, out _);
        vm.IsOverlayCompareEnabled = true;
        var pending = vm.RefreshOverlayWindowAsync();
        await client.PreviewStarted.Task;
        vm.ResetAll();
        client.PreviewRelease.SetResult();
        await pending;
        Assert.Null(vm.CurrentOverlayWindow);
        Assert.Contains("关闭", vm.OverlayStatusText);
    }

    [Fact]
    public async Task Inspections_ShowActualReturnedCoverage()
    {
        using var vm = CreateViewModel(new FakePreprocessingClient(), out _);
        await vm.InspectBadChannelsAsync();
        Assert.Contains("0–60 s", vm.StatusMessage);
        await vm.InspectArtifactSegmentsAsync();
        Assert.Contains("0–60 s", vm.CleanRatioSummary);
        Assert.DoesNotContain("整段信号干净", vm.StatusMessage);
    }

    [Fact]
    public async Task ChangedInputs_DiscardLateIcaAndPreventConfirmation()
    {
        var client = new ControlledClient { DelayIca = true };
        using var vm = CreateViewModel(client, out _);
        var pending = vm.InspectIcaForDialogAsync();
        await client.IcaStarted.Task;
        vm.InvalidateSignalInputs();
        client.IcaRelease.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(vm.IcaComponents);
        Assert.Throws<InvalidOperationException>(() => vm.ConfirmIcaSelection([0], "fp_abc123456789"));
    }

    [Fact]
    public async Task Playback_DiscardsPreviewAndPausingAllowsNewPreview()
    {
        var client = new ControlledClient { DelayPreview = true };
        using var vm = CreateViewModel(client, out _);
        vm.IsOverlayCompareEnabled = true;
        var pending = vm.RefreshOverlayWindowAsync();
        await client.PreviewStarted.Task;
        vm.SetPlaybackActive(true);
        client.PreviewRelease.SetResult();
        await pending;
        Assert.Null(vm.CurrentOverlayWindow);
        vm.SetPlaybackActive(false);
        await vm.RefreshOverlayWindowAsync();
        Assert.NotNull(vm.CurrentOverlayWindow);
    }

    [Fact]
    public async Task Reset_DiscardsBadChannelAndArtifactInspectionResponses()
    {
        var client = new ControlledClient { DelayScan = true };
        using var vm = CreateViewModel(client, out _);
        var bads = vm.InspectBadChannelsAsync();
        await client.ScanStarted.Task;
        vm.ResetAll();
        client.ScanRelease.SetResult();
        await bads;
        Assert.Empty(vm.BadChannelRows);
        var artifactClient = new ControlledClient { DelayScan = true };
        using var artifactVm = CreateViewModel(artifactClient, out _);
        var artifacts = artifactVm.InspectArtifactSegmentsAsync();
        await artifactClient.ScanStarted.Task;
        artifactVm.ResetAll();
        artifactClient.ScanRelease.SetResult();
        await artifacts;
        Assert.Empty(artifactVm.ArtifactSegments);
        Assert.Contains("已重置", artifactVm.StatusMessage);
    }

    [Fact]
    public async Task LongRecording_UsesActualResourceLimitedInspectionRange()
    {
        var client = new ControlledClient { ReturnedScanEnd = 37.5 };
        using var vm = new BrainPlatform.Desktop.Modules.Review.Preprocessing.ReviewPreprocessingViewModel(
            _ => Task.FromResult<(string, string?, double)>(("rec-1", "montage-avg", 7200)),
            () => client, _ => { }, () => (0, 10, 1, 30, 50));
        await vm.InspectBadChannelsAsync();
        Assert.Contains("0–37.5 s", vm.StatusMessage);
        await vm.InspectArtifactSegmentsAsync();
        Assert.Contains("0–37.5 s", vm.CleanRatioSummary);
        Assert.Equal(180, client.LastScanDuration);
    }

    [Fact]
    public async Task TreatmentBadge_OnlyClaimsInterpolationWithCurrentVerifiedPreview()
    {
        using var vm = CreateViewModel(new FakePreprocessingClient(), out _);
        await vm.InspectBadChannelsAsync();
        vm.IsOverlayCompareEnabled = true;
        await vm.RefreshOverlayWindowAsync();
        Assert.Equal(BrainPlatform.Desktop.Modules.Review.Preprocessing.ChannelVisualPreprocessingState.Interpolated,
            vm.ResolveChannelBadge("C4").State);
        vm.IsOverlayCompareEnabled = false;
        Assert.Contains("待预览验证", vm.ResolveChannelBadge("C4").Tooltip);
    }

    [Fact]
    public async Task PreviewContract_RejectsMismatchedTimesAndUnknownCompression()
    {
        var client = new FakePreprocessingClient();
        using var vm = CreateViewModel(client, out _);
        vm.IsOverlayCompareEnabled = true;
        await vm.RefreshOverlayWindowAsync();
        var preview = vm.CurrentOverlayWindow!;
        preview.Validate();
        Assert.Throws<InvalidOperationException>(() => (preview with
        {
            TraceTimesSeconds = new Dictionary<string, IReadOnlyList<double>> { ["Fp1"] = [10.0] },
        }).Validate());
        Assert.Throws<InvalidOperationException>(() => (preview with
        {
            DisplayCompression = new PreviewDisplayCompression("uniform_decimation", false, 1000, false),
        }).Validate());
        Assert.Contains("STFT", vm.ArtifactRemovalScopeNotice);
        Assert.Contains("TNK", vm.ArtifactRemovalScopeNotice);
        Assert.Contains("不应用 ICA", vm.ArtifactRemovalScopeNotice);
        var panel = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "ReviewPreprocessingPanel.xaml"));
        Assert.Contains(panel.Descendants(), element => element.Name.LocalName == "TextBlock" &&
            (string?)element.Attribute("Text") == "{Binding ArtifactRemovalScopeNotice}");
    }

    [Fact]
    public async Task ViewportChange_DiscardsLateResponseAndLoadsCurrentAbsoluteWindow()
    {
        var client = new ControlledClient { DelayPreview = true };
        var start = 10.0;
        using var vm = new BrainPlatform.Desktop.Modules.Review.Preprocessing.ReviewPreprocessingViewModel(
            _ => Task.FromResult<(string, string?, double)>(("rec-1", "montage-avg", 60)),
            () => client, _ => { }, () => (start, 10, 1, 30, 50));
        vm.IsOverlayCompareEnabled = true;
        var old = vm.RefreshOverlayWindowAsync();
        await client.PreviewStarted.Task;
        start = 20;
        var current = vm.RefreshOverlayWindowAsync();
        client.PreviewRelease.SetResult();
        await Task.WhenAll(old, current);
        Assert.Equal(20, vm.CurrentOverlayWindow!.StartSeconds);
        Assert.Equal(20, vm.CurrentOverlayWindow.TraceTimesSeconds["Fp1"][0]);
    }

    [Fact]
    public async Task UpstreamChange_InvalidatesPendingIcaBeforeConfirmation()
    {
        using var vm = CreateViewModel(new FakePreprocessingClient(), out _);
        var pending = await vm.InspectIcaForDialogAsync();
        vm.PeakToPeakUv += 50;
        Assert.Throws<InvalidOperationException>(() => vm.ConfirmIcaSelection([0], pending.IcaFitFingerprint));
        Assert.Null(vm.IcaFitFingerprint);
    }

    [Fact]
    public async Task Dispose_DiscardsLatePreviewAndLateFailureDoesNotOverwriteResetStatus()
    {
        var client = new ControlledClient { DelayPreview = true };
        var vm = CreateViewModel(client, out _);
        vm.IsOverlayCompareEnabled = true;
        var pending = vm.RefreshOverlayWindowAsync();
        await client.PreviewStarted.Task;
        vm.ResetAll();
        client.FailPreview = true;
        client.PreviewRelease.SetResult();
        await pending;
        Assert.Contains("关闭", vm.OverlayStatusText);
        vm.Dispose();
        Assert.Null(vm.CurrentOverlayWindow);
    }

    [Fact]
    public async Task Dispose_DiscardsLateIcaResponse()
    {
        var client = new ControlledClient { DelayIca = true };
        var vm = CreateViewModel(client, out _);
        var pending = vm.InspectIcaForDialogAsync();
        await client.IcaStarted.Task;
        vm.Dispose();
        client.IcaRelease.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(vm.IcaComponents);
    }

    private sealed class ControlledClient : FakePreprocessingClient
    {
        public string? NextFingerprint { get; set; }
        public bool DelayIca { get; set; }
        public bool DelayPreview { get; set; }
        public bool FailPreview { get; set; }
        public bool DelayScan { get; set; }
        public double? ReturnedScanEnd { get; set; }
        public double LastScanDuration { get; private set; }
        public TaskCompletionSource ScanStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ScanRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource IcaStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource IcaRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PreviewStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PreviewRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<BadChannelInspectionResponse> InspectBadChannelsAsync(
            string recordingId, double startSeconds, double windowSeconds, CancellationToken cancellationToken)
        {
            LastScanDuration = windowSeconds;
            ScanStarted.TrySetResult();
            if (DelayScan) await ScanRelease.Task;
            var result = await base.InspectBadChannelsAsync(recordingId, startSeconds, windowSeconds, cancellationToken);
            return ReturnedScanEnd is { } end ? result with { EndSeconds = end } : result;
        }

        public override async Task<ArtifactSegmentInspectionResponse> InspectArtifactSegmentsAsync(
            string recordingId, double startSeconds, double windowSeconds, string? montageId,
            double? peakToPeakUv, double? muscleZScoreThreshold, IReadOnlyList<string>? badChannels,
            string badChannelPolicy, CancellationToken cancellationToken)
        {
            LastScanDuration = windowSeconds;
            ScanStarted.TrySetResult();
            if (DelayScan) await ScanRelease.Task;
            var result = await base.InspectArtifactSegmentsAsync(recordingId, startSeconds, windowSeconds, montageId,
                peakToPeakUv, muscleZScoreThreshold, badChannels, badChannelPolicy, cancellationToken);
            return ReturnedScanEnd is { } end ? result with { EndSeconds = end } : result;
        }

        public override async Task<IcaInspectionResponse> InspectIcaComponentsAsync(
            string recordingId, string? montageId, double lowHz, double highHz, double? notchHz,
            IReadOnlyList<string>? badChannels, string badChannelPolicy, string artifactPolicy,
            double? peakToPeakUv, double? muscleZScoreThreshold, bool allowFrontalEogProxy,
            int? icaComponentCount, double icaEogThreshold, CancellationToken cancellationToken)
        {
            IcaStarted.TrySetResult();
            if (DelayIca) await IcaRelease.Task;
            var report = await base.InspectIcaComponentsAsync(recordingId, montageId, lowHz, highHz, notchHz,
                badChannels, badChannelPolicy, artifactPolicy, peakToPeakUv, muscleZScoreThreshold,
                allowFrontalEogProxy, icaComponentCount, icaEogThreshold, cancellationToken);
            return NextFingerprint is null ? report : report with { IcaFitFingerprint = NextFingerprint };
        }

        public override async Task<PreprocessingPreviewWindowResponse> PreviewPreprocessedWindowAsync(
            string recordingId, PreprocessingPreviewWindowRequest request, CancellationToken cancellationToken)
        {
            PreviewStarted.TrySetResult();
            if (DelayPreview) await PreviewRelease.Task;
            if (FailPreview) throw new InvalidOperationException("preview failure");
            return await base.PreviewPreprocessedWindowAsync(recordingId, request, cancellationToken);
        }
    }
}
