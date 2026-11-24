namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Fact]
    public async Task OnlineProfileAutoSelectsAndSwitchesBackWithoutChangingAlgorithmSelection()
    {
        var configuration = Configuration();
        var original = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(original, AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(original)], []);
        await using var viewModel = new RecordingReviewViewModel(new FakeReader(Manifest(configuration)), catalog);
        await viewModel.InitializeAsync();
        var algorithm = viewModel.WorkspaceMontageSelection.Capture();
        var temporary = TemporaryMontageFixture.Create(configuration);

        await viewModel.SetTemporaryWaveformMontageAsync(temporary);

        Assert.Same(temporary, viewModel.CurrentViewingMontage);
        Assert.Contains(temporary, viewModel.CompatibleViewingMontages);
        Assert.Same(algorithm, viewModel.WorkspaceMontageSelection.Capture());
        Assert.Equal("在线导联", viewModel.CurrentFrame!.ViewingMontageName);
        Assert.True(viewModel.CurrentFrame.HasTemporaryMontage);

        await viewModel.SelectViewingMontageAsync(original);
        Assert.Same(original, viewModel.CurrentViewingMontage);
        Assert.Same(algorithm, viewModel.WorkspaceMontageSelection.Capture());
        Assert.False(viewModel.CurrentFrame!.HasTemporaryMontage);
        await viewModel.SelectViewingMontageAsync(temporary);
        Assert.Same(temporary, viewModel.CurrentViewingMontage);
        Assert.Same(algorithm, viewModel.WorkspaceMontageSelection.Capture());

        var replacement = temporary with { Name = "新在线配置", Revision = 2 };
        await viewModel.SetTemporaryWaveformMontageAsync(replacement);
        Assert.Same(replacement, viewModel.CurrentViewingMontage);
        Assert.DoesNotContain(temporary, viewModel.CompatibleViewingMontages);
        Assert.Same(algorithm, viewModel.WorkspaceMontageSelection.Capture());
        await viewModel.DisposeAsync();
        Assert.Null(viewModel.TemporaryWaveformMontage);
    }
}
