using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Fact]
    public async Task Analysis_status_notifications_do_not_republish_result_and_detach_clears_panel()
    {
        var configuration = Configuration();
        var profile = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(profile, AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(profile)], []);
        var response = RbpReviewPresentationTests.Ready(5);
        var coordinator = new ReviewAnalysisCoordinator((_, _, _, _) => Task.FromResult(response));
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            analysisCoordinators: [coordinator]);
        await vm.InitializeAsync();
        var updates = 0;
        vm.RbpAnalysis.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.RbpAnalysis.Bands)) updates++;
        };

        await vm.AttachAnalysisAsync("session");
        Assert.Equal(1, updates);
        response = RbpReviewPresentationTests.Ready(6);
        await vm.LoadAnalysisAtCurrentPositionAsync();
        Assert.Equal(2, updates);
        Assert.All(vm.RbpAnalysis.ChannelHistories, history => Assert.Equal(2, history.Windows.Count));
        await vm.DetachAnalysisAsync();
        Assert.Empty(vm.RbpAnalysis.ChannelHistories);
    }
}
