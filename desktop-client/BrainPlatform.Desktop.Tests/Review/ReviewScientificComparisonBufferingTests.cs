using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;
using Xunit;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class ReviewPreprocessingTests
{
    [Fact]
    public async Task ScientificComparison_ScrollingWithinBufferedTilesNeverFlashesColoredAndReusesExactReference()
    {
        var client = new CountingPreviewClient();
        var start = 10.0;
        var window = 10.0;
        using var vm = new ReviewPreprocessingViewModel(
            _ => Task.FromResult<(string, string?, double)>(("rec-1", "montage-avg", 60.0)),
            () => client,
            _ => { },
            () => (start, window, 1.0, 30.0, 50.0),
            getDisplayedChannels: () => ["Fp1", "C4"]);

        vm.IsOverlayCompareEnabled = true;
        await vm.HandleViewportChangedAsync(60.0);
        await vm.PrefetchAdjacentTilesAsync(60.0);

        // Initial tile [10,20] + prefetched neighbors [0,10] and [20,30] are stitched into one [0,30] buffer.
        Assert.NotNull(vm.CurrentOverlayWindow);
        Assert.Equal(0.0, vm.CurrentOverlayWindow!.StartSeconds, precision: 3);
        Assert.Equal(30.0, vm.CurrentOverlayWindow.EndSeconds, precision: 3);
        Assert.True(vm.SuppressesOrdinaryColoredTraces(["Fp1", "C4"]));
        Assert.True(vm.CanDisplayScientificComparison(["Fp1", "C4"]));

        // Scroll smoothly across [10,20] -> [12,22] -> [15,25] -> [18,28]:
        // Always stays in scientific comparison mode (zero colored flash) and covers every viewport immediately.
        foreach (var nextStart in new[] { 12.0, 15.0, 18.0 })
        {
            start = nextStart;
            await vm.HandleViewportChangedAsync(60.0);
            Assert.True(vm.SuppressesOrdinaryColoredTraces(["Fp1", "C4"]));
            Assert.True(vm.CanDisplayScientificComparison(["Fp1", "C4"]));
            Assert.True(vm.CurrentOverlayWindow!.StartSeconds <= nextStart + 0.01);
            Assert.True(vm.CurrentOverlayWindow.EndSeconds >= nextStart + window - 0.01);
        }

        // Now that [0,40] is buffered, scrolling back and forth inside [12,28] issues ZERO extra requests and reuses exact reference!
        var bufferedRef = vm.CurrentOverlayWindow;
        var requestsBeforePan = client.Requests.Count;
        foreach (var nextStart in new[] { 14.0, 16.0, 13.0, 15.0 })
        {
            start = nextStart;
            await vm.HandleViewportChangedAsync(60.0);
            Assert.Same(bufferedRef, vm.CurrentOverlayWindow);
        }
        Assert.Equal(requestsBeforePan, client.Requests.Count);
    }

    [Fact]
    public async Task ScientificComparison_JumpingOutsideCacheKeepsColoredSuppressedUntilLoadedOrFailed()
    {
        var client = new DelayedJumpPreviewClient();
        var start = 0.0;
        using var vm = new ReviewPreprocessingViewModel(
            _ => Task.FromResult<(string, string?, double)>(("rec-1", "montage-avg", 120.0)),
            () => client,
            _ => { },
            () => (start, 10.0, 1.0, 30.0, 50.0),
            getDisplayedChannels: () => ["Fp1", "C4"]);

        vm.IsOverlayCompareEnabled = true;
        await vm.HandleViewportChangedAsync(120.0);
        Assert.NotNull(vm.CurrentOverlayWindow);

        // Jump far outside buffer [0,10] -> [90,100] while transport is delayed.
        client.DelayNext = true;
        start = 90.0;
        var jumpTask = vm.HandleViewportChangedAsync(120.0);
        await client.FetchStarted.Task;

        // While loading the jumped tile: stale [0,10] trace is cleared, BUT ordinary colored traces stay suppressed!
        Assert.Null(vm.CurrentOverlayWindow);
        Assert.True(vm.SuppressesOrdinaryColoredTraces(["Fp1", "C4"]));

        client.FetchRelease.SetResult();
        await jumpTask;
        Assert.NotNull(vm.CurrentOverlayWindow);
        Assert.True(vm.CurrentOverlayWindow!.StartSeconds <= 90.0 + 0.01);
        Assert.True(vm.CurrentOverlayWindow.EndSeconds >= 100.0 - 0.01);
        Assert.True(vm.CanDisplayScientificComparison(["Fp1", "C4"]));

        // If a subsequent jump genuinely fails, ordinary colored waveform MUST be restored cleanly.
        client.FailNext = true;
        start = 110.0;
        await vm.HandleViewportChangedAsync(120.0);
        Assert.Null(vm.CurrentOverlayWindow);
        Assert.False(vm.SuppressesOrdinaryColoredTraces(["Fp1", "C4"]));
        Assert.Contains("已恢复普通彩色阅图", vm.OverlayStatusText);
    }

    [Fact]
    public async Task ScientificComparison_ConfigChangeAndZoomInvalidateCachedTilesImmediately()
    {
        var client = new CountingPreviewClient();
        var start = 10.0;
        var window = 10.0;
        using var vm = new ReviewPreprocessingViewModel(
            _ => Task.FromResult<(string, string?, double)>(("rec-1", "montage-avg", 60.0)),
            () => client,
            _ => { },
            () => (start, window, 1.0, 30.0, 50.0),
            getDisplayedChannels: () => ["Fp1", "C4"]);

        vm.IsOverlayCompareEnabled = true;
        await vm.HandleViewportChangedAsync(60.0);
        var countAfterFirst = client.Requests.Count;

        // Zoom from 10s to 5s -> cache scope changes -> must fetch fresh 5s-density tile.
        window = 5.0;
        await vm.HandleViewportChangedAsync(60.0);
        Assert.True(client.Requests.Count > countAfterFirst);
        Assert.Equal(5.0, client.Requests[^1].WindowSeconds, precision: 3);

        // Changing preprocessing parameter clears buffer and re-fetches.
        var countBeforeConfig = client.Requests.Count;
        vm.ArtifactPolicy = "reject";
        await vm.HandleViewportChangedAsync(60.0);
        Assert.True(client.Requests.Count > countBeforeConfig);
    }

    [Fact]
    public async Task ScientificComparison_ContinuousPlaybackAdvancesAcrossTilesWithoutDroppingOrClearingOnPause()
    {
        var client = new CountingPreviewClient();
        var start = 10.0;
        using var vm = new ReviewPreprocessingViewModel(
            _ => Task.FromResult<(string, string?, double)>(("rec-1", "montage-avg", 60.0)),
            () => client,
            _ => { },
            () => (start, 10.0, 1.0, 30.0, 50.0),
            getDisplayedChannels: () => ["Fp1", "C4"]);

        vm.IsOverlayCompareEnabled = true;
        await vm.HandleViewportChangedAsync(60.0);
        await vm.PrefetchAdjacentTilesAsync(60.0);
        var beforePlay = vm.CurrentOverlayWindow;
        Assert.NotNull(beforePlay);

        vm.SetPlaybackActive(true);
        Assert.Same(beforePlay, vm.CurrentOverlayWindow);
        Assert.True(vm.SuppressesOrdinaryColoredTraces(["Fp1", "C4"]));
        Assert.True(vm.CanDisplayScientificComparison(["Fp1", "C4"]));

        // Advance playhead across multiple tiles during playback: never clears or falls back to colored preview.
        for (var t = 10.5; t <= 25.0; t += 1.5)
        {
            start = t;
            await vm.HandleViewportChangedAsync(60.0);
            Assert.NotNull(vm.CurrentOverlayWindow);
            Assert.True(vm.SuppressesOrdinaryColoredTraces(["Fp1", "C4"]));
            Assert.True(vm.CanDisplayScientificComparison(["Fp1", "C4"]));
            Assert.True(vm.CurrentOverlayWindow!.StartSeconds <= t + 0.01);
            Assert.True(vm.CurrentOverlayWindow.EndSeconds >= t + 10.0 - 0.01);
        }

        // Pausing must NOT clear the waveform or trigger a blank reload flash.
        var beforePause = vm.CurrentOverlayWindow;
        vm.SetPlaybackActive(false);
        Assert.Same(beforePause, vm.CurrentOverlayWindow);
        Assert.True(vm.CanDisplayScientificComparison(["Fp1", "C4"]));
    }

    [Fact]
    public void ScientificComparisonTileCache_MergesContiguousTilesWithoutDuplicateTimestampsAndPreservesNaN()
    {
        var cache = new ScientificComparisonTileCache();
        var t0 = MakeTile(0, 500, 0.0, 1.0, [0.0, 0.5, 1.0], [1.0, null, 2.0]);
        var t1 = MakeTile(500, 1000, 1.0, 2.0, [1.0, 1.5, 2.0], [2.0, 3.0, 4.0]);
        cache.Put(1, 1.0, t0);
        cache.Put(1, 1.0, t1);

        Assert.True(cache.TryComposeForViewport(1, 0.25, 1.0, 10.0, out var merged, out var full));
        Assert.True(full);
        Assert.NotNull(merged);
        Assert.Equal([0.0, 0.5, 1.0, 1.5, 2.0], merged!.Baseline.TraceTimesSeconds["Fp1"]);
        Assert.Equal([1.0, null, 2.0, 3.0, 4.0], merged.Baseline.TracesUv["Fp1"]);
    }

    private static PreprocessingPreviewWindowResponse MakeTile(
        long startSample, long endSample, double startS, double endS,
        IReadOnlyList<double> times, IReadOnlyList<double?> values) =>
        new(
            PreprocessingPreviewWindowResponse.CurrentSchemaVersion,
            "rec-1", "sha", "m1", "msha", startSample, endSample, startS, endS, 500.0, "µV",
            new PreviewFilterEvidence("whole_recording_before_window_slice", "zero_phase", [1.0, 50.0], 50.0, 30.0, "fp"),
            EnhancedPreprocessingActive: false,
            new PreviewDisplayCompression(PreprocessingPreviewWindowResponse.PairedCompressionMethod, false, 1200, true),
            new PreviewBaselineSide("scientific_baseline", ["Fp1"],
                new Dictionary<string, IReadOnlyList<double>> { ["Fp1"] = times },
                new Dictionary<string, IReadOnlyList<double?>> { ["Fp1"] = values }),
            new PreviewProcessedSide("scientific_processed", ["Fp1"], [], [], [],
                new Dictionary<string, IReadOnlyList<double>> { ["Fp1"] = times },
                new Dictionary<string, IReadOnlyList<double?>> { ["Fp1"] = values },
                false, "skipped", null));

    private sealed class CountingPreviewClient : FakePreprocessingClient
    {
        public List<PreprocessingPreviewWindowRequest> Requests { get; } = [];

        public override Task<PreprocessingPreviewWindowResponse> PreviewPreprocessedWindowAsync(
            string recordingId, PreprocessingPreviewWindowRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return base.PreviewPreprocessedWindowAsync(recordingId, request, cancellationToken);
        }
    }

    private sealed class DelayedJumpPreviewClient : FakePreprocessingClient
    {
        public bool DelayNext { get; set; }
        public bool FailNext { get; set; }
        public TaskCompletionSource FetchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FetchRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<PreprocessingPreviewWindowResponse> PreviewPreprocessedWindowAsync(
            string recordingId, PreprocessingPreviewWindowRequest request, CancellationToken cancellationToken)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("资源上限超出");
            }
            if (DelayNext)
            {
                DelayNext = false;
                FetchStarted.TrySetResult();
                await FetchRelease.Task;
            }
            return await base.PreviewPreprocessedWindowAsync(recordingId, request, cancellationToken);
        }
    }
}
