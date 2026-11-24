using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using static BrainPlatform.Desktop.Tests.Review.ReviewSpectralFixture;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Theory]
    [InlineData("custom-frequency-inspection", false)]
    [InlineData("custom-frequency-inspection", true)]
    [InlineData("spectral-values", false)]
    public async Task Playback_prepares_two_windows_without_publishing_and_adopts_backend_projection_at_deadline(
        string panel, bool range)
    {
        var now = 0d;
        var psd = new SpectralProvider("psd");
        var rbp = new SpectralProvider("rbp");
        await using var vm = ClockedSpectralReview(psd, rbp, () => now);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        vm.CustomFrequencyInspection.RangeLowHz = 1;
        vm.CustomFrequencyInspection.RangeHighHz = 3;
        if (range) vm.CustomFrequencyInspection.Mode = ReviewCustomFrequencyMode.Range;
        await vm.OpenAnalysisAsync(panel);
        await NumericReady(vm, panel, "psd-10000");
        vm.TogglePlayback();
        await Until(() => psd.Cursors.Count == 3 &&
            (panel == "spectral-values" ? psd.Projections == 3 : psd.CustomBinLoads == 3));

        Assert.Equal(new long[] { 10000, 10500, 11000 }, psd.Cursors);
        Assert.Equal(psd.Cursors, rbp.Cursors);
        Assert.Equal(10000, vm.CurrentAnalysisWindow!.CursorSample);
        Assert.Contains("psd-10000", panel == "spectral-values" ? vm.SpectralValues.ProvenanceText : vm.CustomFrequencyInspection.ProvenanceText);

        // Stop the next preparation: publication of 21 s must use the already-returned projection.
        rbp.DeferWindow = true;
        now = 1.05;
        vm.TickPlayback();
        await NumericReady(vm, panel, "psd-10500");
        Assert.Equal(10500, vm.CurrentAnalysisWindow!.CursorSample);
        Assert.Equal(21.05, vm.PositionSeconds, 3);
        Assert.Equal(1, psd.Cursors.Count(sample => sample == 10500));
        Assert.Equal(panel == "spectral-values" ? 3 : 0, psd.Projections);
        Assert.Equal(panel == "custom-frequency-inspection" ? 3 : 0, psd.CustomBinLoads);
        Assert.Equal(range ? 3 : 0, psd.CustomRangeLoads);
        rbp.WindowRelease.TrySetResult();
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("seek")]
    [InlineData("drag")]
    [InlineData("close")]
    public async Task Playback_invalidates_cold_future_work_and_rejects_late_results(string action)
    {
        var now = 0d;
        var psd = new SpectralProvider("psd");
        var rbp = new SpectralProvider("rbp");
        await using var vm = ClockedSpectralReview(psd, rbp, () => now);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        await vm.OpenAnalysisAsync("custom-frequency-inspection");
        await CustomFrequencyReady(vm, "psd-10000");
        rbp.DeferWindow = true;
        vm.TogglePlayback();
        await rbp.WindowStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var futureToken = rbp.WindowToken;
        Assert.Equal(10000, vm.CurrentAnalysisWindow!.CursorSample);
        if (action == "pause") vm.TogglePlayback();
        else if (action == "seek") { rbp.DeferWindow = false; await vm.SeekAsync(30); }
        else if (action == "drag") vm.BeginTimelineDrag();
        else await vm.CloseAnalysisAsync();
        Assert.True(futureToken.IsCancellationRequested);
        rbp.WindowRelease.TrySetResult();
        if (action == "seek") await CustomFrequencyReady(vm, "psd-15000");
        await Task.Delay(40);
        Assert.DoesNotContain("psd-10500", vm.CustomFrequencyInspection.ProvenanceText);
        Assert.DoesNotContain(11000, psd.Cursors);
        if (action == "pause")
        {
            vm.TogglePlayback();
            now = 1.05;
            vm.TickPlayback();
            await CustomFrequencyReady(vm, "psd-10500");
        }
    }

    private static RecordingReviewViewModel ClockedSpectralReview(SpectralProvider psd, SpectralProvider rbp, Func<double> clock) =>
        new(new FakeReader(Manifest(Configuration()), 100),
            new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
                new RawSignalViewDefinition(ChannelNames), [], []), recordingDirectory: "_temp/spectral-preparation-fixture",
            analysisCoordinators: [new ReviewAnalysisCoordinator(psd), new ReviewAnalysisCoordinator(rbp)],
            playbackClockSeconds: clock);

    [Fact]
    public async Task Range_edit_reuses_prepared_bins_but_projects_new_bounds_for_the_published_window()
    {
        var now = 0d;
        var psd = new SpectralProvider("psd");
        var rbp = new SpectralProvider("rbp");
        await using var vm = ClockedSpectralReview(psd, rbp, () => now);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        vm.CustomFrequencyInspection.RangeLowHz = 1;
        vm.CustomFrequencyInspection.RangeHighHz = 3;
        vm.CustomFrequencyInspection.Mode = ReviewCustomFrequencyMode.Range;
        await vm.OpenAnalysisAsync("custom-frequency-inspection");
        await CustomFrequencyReady(vm, "psd-10000");
        vm.TogglePlayback();
        await Until(() => psd.CustomRangeLoads == 3);
        vm.CustomFrequencyInspection.RangeHighHz = 4;
        await Until(() => psd.CustomRangeLoads == 4 &&
            vm.CustomFrequencyInspection.Rows.All(row => row.AbsolutePowerUv2 is not null));

        rbp.DeferWindow = true;
        now = 1.05;
        vm.TickPlayback();
        await CustomFrequencyReady(vm, "psd-10500");
        await Until(() => psd.CustomRangeLoads == 5 &&
            vm.CustomFrequencyInspection.Rows.All(row => row.AbsolutePowerUv2 is not null));
        Assert.Equal((1d, 4d), psd.CustomRanges.Last());
        Assert.Equal(3, psd.CustomBinLoads);
        Assert.Equal(1, psd.Cursors.Count(sample => sample == 10500));
        Assert.DoesNotContain("不可用", vm.CustomFrequencyInspection.StatusText);
        rbp.WindowRelease.TrySetResult();
    }

    private static Task NumericReady(RecordingReviewViewModel vm, string panel, string windowId) =>
        panel == "spectral-values" ? SpectralReady(vm, windowId) : CustomFrequencyReady(vm, windowId);

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }
}

public sealed class ReviewSpectralPlaybackBufferTests
{
    [Fact]
    public async Task Cold_preparation_is_serial_bounded_and_consumed_without_recalculation()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<long>();
        using var buffer = new ReviewPlaybackBuffer<ReviewPreparedSpectralWindow>(async (sample, token) =>
        {
            requests.Add(sample);
            await release.Task.WaitAsync(token);
            return new ReviewPreparedSpectralWindow(Window("psd", sample), Window("rbp", sample));
        }, CancellationToken.None);
        buffer.Advance(10000, 500, 50000);
        Assert.Equal(new long[] { 10500 }, requests);
        var current = buffer.Take(10500)!;
        Assert.False(current.IsCompleted);
        release.TrySetResult();
        Assert.Equal(10500, (await current.WaitAsync(TimeSpan.FromSeconds(3))).Psd.CursorSample);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (requests.Count < 2) await Task.Delay(5, timeout.Token);
        Assert.Equal(new long[] { 10500, 11000 }, requests);
        Assert.Equal(11000, (await buffer.Take(11000)!).Psd.CursorSample);
        Assert.Null(buffer.Take(11500));
    }

    [Fact]
    public async Task Cancellation_discards_a_result_even_when_the_source_ignores_the_token()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var buffer = new ReviewPlaybackBuffer<ReviewPreparedSpectralWindow>(async (sample, _) =>
        {
            await release.Task;
            return new ReviewPreparedSpectralWindow(Window("psd", sample), Window("rbp", sample));
        }, CancellationToken.None);
        buffer.Advance(10000, 500, 50000);
        var task = buffer.Take(10500)!;
        buffer.Dispose();
        release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Null(buffer.Take(11000));
    }

    [Fact]
    public async Task Missed_deadlines_discard_obsolete_queued_preparation_before_it_starts()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<long>();
        using var buffer = new ReviewPlaybackBuffer<ReviewPreparedSpectralWindow>(async (sample, token) =>
        {
            requests.Add(sample);
            await release.Task.WaitAsync(token);
            return new ReviewPreparedSpectralWindow(Window("psd", sample), Window("rbp", sample));
        }, CancellationToken.None);
        buffer.Advance(10000, 500, 50000);
        var active = buffer.Take(10500)!;
        buffer.DiscardBefore(12000);
        release.TrySetResult();
        await active.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(buffer.Take(11000));
        Assert.Equal(new long[] { 10500 }, requests);
    }

    [Fact]
    public async Task Failed_future_work_is_observed_and_returned_to_the_consumer()
    {
        using var buffer = new ReviewPlaybackBuffer<ReviewPreparedSpectralWindow>((_, _) =>
            Task.FromException<ReviewPreparedSpectralWindow>(new InvalidOperationException("source failed")), CancellationToken.None);
        buffer.Advance(49500, 500, 50000);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => buffer.Take(50000)!);
        Assert.Equal("source failed", error.Message);
        Assert.Null(buffer.Take(50500));
    }
}
