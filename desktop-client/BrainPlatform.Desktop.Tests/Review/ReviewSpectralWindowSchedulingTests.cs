using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using static BrainPlatform.Desktop.Tests.Review.ReviewSpectralFixture;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Theory]
    [InlineData("custom-frequency-inspection")]
    [InlineData("spectral-values")]
    public async Task First_open_at_fractional_second_projects_actual_aligned_backend_window(string panel)
    {
        var psd = new SpectralProvider("psd") { AlignWindowToStep = true };
        var rbp = new SpectralProvider("rbp") { AlignWindowToStep = true };
        await using var vm = SpectralReview(psd, rbp);
        await vm.InitializeAsync();
        await vm.SeekAsync(20.352);
        await vm.OpenAnalysisAsync(panel);
        if (panel == "custom-frequency-inspection") await CustomFrequencyReady(vm, "psd-10000");
        else await SpectralReady(vm, "psd-10000");

        Assert.Equal(10176, Assert.Single(psd.Cursors));
        Assert.Equal(psd.Cursors, rbp.Cursors);
        Assert.Equal(10000, vm.CurrentAnalysisWindow!.CursorSample);
        Assert.False(vm.IsAnalysisTargetDisplayed);
    }

    [Fact]
    public async Task Spectral_first_load_cancelled_by_seek_releases_preparation_without_publishing_old_pair()
    {
        var psd = new SpectralProvider("psd") { DeferWindow = true };
        var rbp = new SpectralProvider("rbp") { DeferWindow = true };
        await using var vm = SpectralReview(psd, rbp);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        var opening = vm.OpenAnalysisAsync("custom-frequency-inspection");
        await Task.WhenAll(psd.WindowStarted.Task, rbp.WindowStarted.Task).WaitAsync(TimeSpan.FromSeconds(3));
        var oldPsdToken = psd.WindowToken;
        var oldRbpToken = rbp.WindowToken;
        var published = new List<string>();
        vm.CustomFrequencyInspection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReviewCustomFrequencyInspectionViewModel.ProvenanceText))
                published.Add(vm.CustomFrequencyInspection.ProvenanceText);
        };

        await vm.SeekAsync(30).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(oldPsdToken.IsCancellationRequested);
        Assert.True(oldRbpToken.IsCancellationRequested);
        Assert.Equal(30, vm.PositionSeconds);
        Assert.False(vm.CustomFrequencyInspection.HasBins);
        psd.WindowRelease.TrySetResult();
        rbp.WindowRelease.TrySetResult();
        await opening.WaitAsync(TimeSpan.FromSeconds(3));
        await CustomFrequencyReady(vm, "psd-15000");

        Assert.DoesNotContain(published, value => value.Contains("psd-10000", StringComparison.Ordinal));
        Assert.Equal(new long[] { 10000, 15000 }, psd.Cursors);
        Assert.Equal(psd.Cursors, rbp.Cursors);
        Assert.DoesNotContain("准备失败", vm.AnalysisPreparationText);
        Assert.NotNull(vm.CurrentFrame);
    }

    [Fact]
    public async Task Playback_retains_pair_while_slow_rbp_finishes_and_coalesces_both_sources_to_latest_target()
    {
        var now = 0d;
        var psd = new SpectralProvider("psd");
        var rbp = new SpectralProvider("rbp");
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(Configuration()), 100),
            new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
                new RawSignalViewDefinition(ChannelNames), [], []), recordingDirectory: "_temp/spectral-slow-fixture",
            analysisCoordinators: [new ReviewAnalysisCoordinator(psd), new ReviewAnalysisCoordinator(rbp)],
            playbackClockSeconds: () => now);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        await vm.OpenAnalysisAsync("custom-frequency-inspection");
        await CustomFrequencyReady(vm, "psd-10000");
        vm.CustomFrequencyInspection.SelectedFrequencyHz = 2;
        var rows = vm.CustomFrequencyInspection.Rows.ToArray();
        var curve = vm.CustomFrequencyInspection.PsdCurve;
        var published = new List<string>();
        vm.CustomFrequencyInspection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReviewCustomFrequencyInspectionViewModel.ProvenanceText))
                published.Add(vm.CustomFrequencyInspection.ProvenanceText);
        };
        rbp.DeferWindow = true;
        vm.TogglePlayback();
        now = 1.25;
        vm.TickPlayback();
        await rbp.WindowStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (var second = 2; second <= 4; second++)
        {
            now = second + 0.25;
            vm.TickPlayback();
        }

        Assert.Equal(24.25, vm.PositionSeconds);
        Assert.False(rbp.WindowToken.IsCancellationRequested);
        Assert.Equal(new long[] { 10000, 10500 }, psd.Cursors);
        Assert.Equal(psd.Cursors, rbp.Cursors);
        Assert.True(vm.CustomFrequencyInspection.HasBins);
        Assert.Same(curve, vm.CustomFrequencyInspection.PsdCurve);
        Assert.Contains("10000", vm.CustomFrequencyInspection.ProvenanceText);
        Assert.Contains("当前仍显示", vm.CustomFrequencyInspection.StatusText);
        Assert.All(rows, row => Assert.NotNull(row.AbsolutePowerUv2));
        rbp.WindowRelease.TrySetResult();
        await CustomFrequencyReady(vm, "psd-12000");

        Assert.Contains(published, value => value.Contains("psd-10500", StringComparison.Ordinal));
        Assert.Equal(new long[] { 10000, 10500, 12000 }, psd.Cursors);
        Assert.Equal(psd.Cursors, rbp.Cursors);
        Assert.DoesNotContain("", published);
        Assert.Equal(3, psd.CustomBinLoads);
        for (var i = 0; i < rows.Length; i++) Assert.Same(rows[i], vm.CustomFrequencyInspection.Rows[i]);
        Assert.NotNull(vm.CurrentFrame);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Playback_finishes_active_bin_or_range_projection_before_starting_latest_pair(bool rangeMode)
    {
        var now = 0d;
        var psd = new SpectralProvider("psd") { DeferCustomBins = !rangeMode, DeferCustomRange = rangeMode };
        var rbp = new SpectralProvider("rbp");
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(Configuration()), 100),
            new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
                new RawSignalViewDefinition(ChannelNames), [], []), recordingDirectory: "_temp/spectral-projection-fixture",
            analysisCoordinators: [new ReviewAnalysisCoordinator(psd), new ReviewAnalysisCoordinator(rbp)],
            playbackClockSeconds: () => now);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        vm.CustomFrequencyInspection.SelectedFrequencyHz = 2;
        if (rangeMode) vm.CustomFrequencyInspection.Mode = ReviewCustomFrequencyMode.Range;
        await vm.OpenAnalysisAsync("custom-frequency-inspection").WaitAsync(TimeSpan.FromSeconds(3));
        await (rangeMode ? psd.CustomRangeStarted.Task : psd.CustomBinsStarted.Task)
            .WaitAsync(TimeSpan.FromSeconds(3));
        vm.TogglePlayback();
        now = 1.25;
        vm.TickPlayback();
        now = 4.25;
        vm.TickPlayback();

        Assert.Single(psd.Cursors);
        Assert.Equal(psd.Cursors, rbp.Cursors);
        Assert.False(rangeMode ? psd.CustomRangeToken.IsCancellationRequested : psd.CustomBinsToken.IsCancellationRequested);
        Assert.Equal(24.25, vm.PositionSeconds);
        psd.CustomBinsRelease.TrySetResult();
        psd.CustomRangeRelease.TrySetResult();
        await CustomFrequencyReady(vm, "psd-12000");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (vm.CustomFrequencyInspection.Rows.Any(row => row.AbsolutePowerUv2 is null))
            await Task.Delay(10, timeout.Token);

        Assert.Equal(new long[] { 10000, 12000 }, psd.Cursors);
        Assert.Equal(psd.Cursors, rbp.Cursors);
        Assert.Equal(2, psd.CustomBinLoads);
        Assert.Equal(rangeMode ? 2 : 0, psd.CustomRangeLoads);
    }

    [Fact]
    public async Task Spectral_range_edit_waits_for_pending_bins_before_projecting_the_new_source_pair()
    {
        var now = 0d;
        var psd = new SpectralProvider("psd") { DeferCustomBins = true, DeferredCustomBinsLoad = 2 };
        var rbp = new SpectralProvider("rbp");
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(Configuration()), 100),
            new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
                new RawSignalViewDefinition(ChannelNames), [], []), recordingDirectory: "_temp/spectral-range-fixture",
            analysisCoordinators: [new ReviewAnalysisCoordinator(psd), new ReviewAnalysisCoordinator(rbp)],
            playbackClockSeconds: () => now);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        vm.CustomFrequencyInspection.RangeLowHz = 1;
        vm.CustomFrequencyInspection.RangeHighHz = 3;
        await vm.OpenAnalysisAsync("custom-frequency-inspection");
        await CustomFrequencyReady(vm, "psd-10000");
        vm.TogglePlayback();
        now = 1.25;
        vm.TickPlayback();
        await psd.CustomBinsStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(vm.CustomFrequencyInspection.HasBins);
        Assert.Contains("psd-10000", vm.CustomFrequencyInspection.ProvenanceText);
        vm.CustomFrequencyInspection.Mode = ReviewCustomFrequencyMode.Range;
        await Task.Delay(250);
        Assert.Equal(0, psd.CustomRangeLoads);
        psd.CustomBinsRelease.TrySetResult();
        await CustomFrequencyReady(vm, "psd-10500");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (vm.CustomFrequencyInspection.Rows.Any(row => row.AbsolutePowerUv2 is null))
            await Task.Delay(10, timeout.Token);

        Assert.Equal(1, psd.CustomRangeLoads);
        Assert.Equal((1d, 3d), Assert.Single(psd.CustomRanges));
        Assert.DoesNotContain("不可用", vm.CustomFrequencyInspection.StatusText);
    }
}
