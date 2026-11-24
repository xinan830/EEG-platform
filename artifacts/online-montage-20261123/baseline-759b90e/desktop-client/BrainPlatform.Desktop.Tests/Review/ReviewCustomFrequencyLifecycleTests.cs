using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Modules.Review.ViewModels;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Fact]
    public async Task Custom_range_translation_debounces_both_boundaries_into_one_complete_projection()
    {
        var psd = new SpectralProvider("psd") { DeferCustomRange = true };
        var rbp = new SpectralProvider("rbp");
        await using var vm = SpectralReview(psd, rbp);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        await vm.OpenAnalysisAsync("custom-frequency-inspection");
        await CustomFrequencyReady(vm, "psd-10000");
        vm.CustomFrequencyInspection.Mode = ReviewCustomFrequencyMode.Range;
        await psd.CustomRangeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.CustomFrequencyInspection.SetRange(2, 4);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!vm.CustomFrequencyInspection.StatusText.Contains("2–4", StringComparison.Ordinal))
            await Task.Delay(10, timeout.Token);
        Assert.Equal(new[] { (1d, 3d), (2d, 4d) }, psd.CustomRanges);
        Assert.True(psd.CustomRangeToken.IsCancellationRequested);
        Assert.Single(psd.Cursors);
        Assert.Single(rbp.Cursors);
        Assert.Equal(1, psd.CustomBinLoads);
        Assert.All(vm.CustomFrequencyInspection.Rows, row => Assert.NotNull(row.AbsolutePowerUv2));
    }

    [Fact]
    public async Task Custom_cursor_and_channel_changes_select_cached_bins_without_new_requests()
    {
        var psd = new SpectralProvider("psd");
        var rbp = new SpectralProvider("rbp");
        await using var vm = SpectralReview(psd, rbp);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        await vm.OpenAnalysisAsync("custom-frequency-inspection");
        await CustomFrequencyReady(vm, "psd-10000");
        var rows = vm.CustomFrequencyInspection.Rows.ToArray();

        for (var i = 0; i < 100; i++) vm.CustomFrequencyInspection.SelectedFrequencyHz = 1 + i % 4;
        vm.CustomFrequencyInspection.SelectedChannel = rows[1].Channel;

        Assert.Single(psd.Cursors);
        Assert.Single(rbp.Cursors);
        Assert.Equal(1, psd.CustomBinLoads);
        Assert.Equal(0, psd.CustomRangeLoads);
        Assert.Equal(0, psd.Projections);
        Assert.Equal(ReviewAnalysisPanelKind.CustomFrequencyInspection, vm.ActiveAnalysisPanelKind);
        Assert.False(vm.IsPsdAnalysisPanelOpen);
        Assert.False(vm.IsSpectralValuesPanelOpen);
        Assert.Empty(vm.PsdAnalysis.Points);
        for (var i = 0; i < rows.Length; i++) Assert.Same(rows[i], vm.CustomFrequencyInspection.Rows[i]);
    }

    [Fact]
    public async Task Custom_range_debounces_edits_and_rejects_late_responses_after_new_selection_and_close()
    {
        var psd = new SpectralProvider("psd") { DeferCustomRange = true, IgnoreCustomCancellation = true };
        var rbp = new SpectralProvider("rbp");
        await using var vm = SpectralReview(psd, rbp);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        await vm.OpenAnalysisAsync("custom-frequency-inspection");
        await CustomFrequencyReady(vm, "psd-10000");
        vm.CustomFrequencyInspection.Mode = ReviewCustomFrequencyMode.Range;
        vm.CustomFrequencyInspection.RangeLowHz = 1.1;
        vm.CustomFrequencyInspection.RangeHighHz = 2.9;
        await psd.CustomRangeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal((1d, 3d), Assert.Single(psd.CustomRanges));
        vm.CustomFrequencyInspection.RangeHighHz = 3.9;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!vm.CustomFrequencyInspection.StatusText.Contains("4", StringComparison.Ordinal))
            await Task.Delay(10, timeout.Token);
        Assert.True(psd.CustomRangeToken.IsCancellationRequested);
        Assert.Equal(2, psd.CustomRangeLoads);
        psd.CustomRangeRelease.TrySetResult();
        await Task.Delay(30);
        Assert.Contains("4", vm.CustomFrequencyInspection.StatusText);
        vm.CustomFrequencyInspection.RangeLowHz = 4;
        Assert.All(vm.CustomFrequencyInspection.Rows, row => Assert.Null(row.AbsolutePowerUv2));
        Assert.False(vm.CustomFrequencyInspection.IsRangeValid);
        await vm.CloseAnalysisAsync();
        Assert.False(vm.CustomFrequencyInspection.HasBins);
    }

    [Fact]
    public async Task Late_custom_bins_do_not_overwrite_new_playback_window_or_closed_panel()
    {
        var psd = new SpectralProvider("psd") { DeferCustomBins = true };
        var rbp = new SpectralProvider("rbp");
        await using var vm = SpectralReview(psd, rbp);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        await vm.OpenAnalysisAsync("custom-frequency-inspection");
        await psd.CustomBinsStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await vm.SeekAsync(30);
        await CustomFrequencyReady(vm, "psd-15000");
        Assert.True(psd.CustomBinsToken.IsCancellationRequested);
        psd.CustomBinsRelease.TrySetResult();
        await Task.Delay(30);
        Assert.Contains("psd-15000", vm.CustomFrequencyInspection.ProvenanceText);
        await vm.CloseAnalysisAsync();
        Assert.False(vm.CustomFrequencyInspection.HasBins);
        Assert.All(vm.CustomFrequencyInspection.Rows, row => Assert.Null(row.AbsolutePowerUv2));
        Assert.True(psd.Releases > 0);
        Assert.True(rbp.Releases > 0);
    }

    [Fact]
    public async Task Closing_custom_panel_cancels_pending_range_and_discards_even_uncancellable_response()
    {
        var psd = new SpectralProvider("psd") { DeferCustomRange = true, IgnoreCustomCancellation = true };
        var rbp = new SpectralProvider("rbp");
        await using var vm = SpectralReview(psd, rbp);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        await vm.OpenAnalysisAsync("custom-frequency-inspection");
        await CustomFrequencyReady(vm, "psd-10000");
        vm.CustomFrequencyInspection.Mode = ReviewCustomFrequencyMode.Range;
        await psd.CustomRangeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await vm.CloseAnalysisAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(psd.CustomRangeToken.IsCancellationRequested);
        psd.CustomRangeRelease.TrySetResult();
        await Task.Delay(30);
        Assert.False(vm.CustomFrequencyInspection.HasBins);
        Assert.All(vm.CustomFrequencyInspection.Rows, row => Assert.Null(row.AbsolutePowerUv2));
    }
}
