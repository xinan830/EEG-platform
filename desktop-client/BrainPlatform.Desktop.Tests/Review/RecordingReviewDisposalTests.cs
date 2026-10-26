namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Fact]
    public async Task Queued_playback_tick_after_disposal_does_not_access_session()
    {
        var configuration = Configuration();
        var profile = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(profile, AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(profile)], []);
        var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog);
        await vm.InitializeAsync();
        vm.TogglePlayback();

        await vm.DisposeAsync();
        var position = vm.PositionSeconds;
        vm.TickPlayback();
        await vm.DisposeAsync();

        Assert.False(vm.IsPlaying);
        Assert.Equal(position, vm.PositionSeconds);
    }
}
