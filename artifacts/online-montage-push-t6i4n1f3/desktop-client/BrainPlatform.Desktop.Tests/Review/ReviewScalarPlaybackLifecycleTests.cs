using BrainPlatform.Desktop.Modules.Review.Analysis;
using static BrainPlatform.Desktop.Tests.Review.ReviewSpectralFixture;
using static BrainPlatform.Desktop.Tests.Review.ReviewScalarPlaybackTests;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Fact]
    public async Task Iapf_playback_preparation_follows_viewmodel_play_pause_drag_and_close()
    {
        var now = 0d;
        var provider = new ScalarPlaybackProvider("iapf") { Automatic = true };
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(Configuration()), 100),
            new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
                new RawSignalViewDefinition(ChannelNames), [], []), recordingDirectory: "_temp/scalar-playback-fixture",
            analysisCoordinators: [new ReviewAnalysisCoordinator(provider)], playbackClockSeconds: () => now);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        await vm.OpenAnalysisAsync("iapf");
        Assert.Single(provider.Calls);
        vm.TogglePlayback();
        await Until(() => provider.Calls.Count == 3);
        Assert.Equal(new long[] { 10000, 10500, 11000 }, provider.Samples);
        Assert.Equal(10000, vm.CurrentAnalysisWindow!.CursorSample);
        now = 1;
        vm.TickPlayback();
        await Until(() => vm.CurrentAnalysisWindow!.CursorSample == 10500);
        Assert.Single(provider.Samples, sample => sample == 10500);
        vm.TogglePlayback();

        provider.Automatic = false;
        vm.TogglePlayback();
        var future = provider.Calls.Last();
        vm.BeginTimelineDrag();
        Assert.True(future.Token.IsCancellationRequested);
        vm.PreviewSeek(40);
        vm.EndTimelineDrag();
        var selected = provider.Calls.Last();
        Assert.Equal(20000, selected.Sample);
        var selectedCallCount = provider.Calls.Count;
        selected.Complete();
        await Until(() => vm.CurrentAnalysisWindow!.CursorSample == 20000);
        future.Complete();
        await Until(() => future.Finished);
        Assert.Equal(20000, vm.CurrentAnalysisWindow!.CursorSample);
        await Until(() => provider.Calls.Count > selectedCallCount);
        var closing = provider.Calls.Last();
        Assert.Equal(20500, closing.Sample);
        await vm.DetachAnalysisAsync();
        Assert.True(closing.Token.IsCancellationRequested);
        closing.Complete();
        await Until(() => closing.Finished);
        Assert.Null(vm.CurrentAnalysisWindow);
    }
}
