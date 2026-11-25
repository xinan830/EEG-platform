using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

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
        var montage = new HistoricalMontageDescriptor(profile.Id, "r1", "fingerprint", profile.Name, true,
            true, null, [new("F3-REF", "F3-REF", "F3", "V", true, null)], profile.Id);
        var provider = new PanelProvider("rbp") { MontageCatalogue = [montage] };
        var coordinator = new ReviewAnalysisCoordinator(provider);
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            recordingDirectory: "_temp/analysis-presentation-fixture", analysisCoordinators: [coordinator]);
        await vm.InitializeAsync();
        await vm.SeekAsync(10);
        var updates = 0;
        vm.RbpAnalysis.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.RbpAnalysis.Bands) && vm.RbpAnalysis.HasResult) updates++;
        };

        await vm.OpenAnalysisAsync("rbp");
        Assert.True(vm.IsRbpAnalysisPanelOpen);
        Assert.Equal(1, updates);
        await vm.SeekAsync(11);
        Assert.Equal(2, updates);
        await vm.LoadAnalysisAtCurrentPositionAsync();
        Assert.Equal(3, updates);
        Assert.Equal(provider.Loads, updates);
        Assert.NotEmpty(vm.RbpAnalysis.ChannelHistories);
        Assert.All(vm.RbpAnalysis.ChannelHistories, history => Assert.Equal(2, history.Windows.Count));
        await vm.DetachAnalysisAsync();
        Assert.Empty(vm.RbpAnalysis.ChannelHistories);
    }
}
