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
        var transitionFrames = new List<(bool Compatible, bool TemporaryFrame)>();
        viewModel.Preprocessing.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.Preprocessing.IsDisplayMontageCompatible))
                transitionFrames.Add((viewModel.Preprocessing.IsDisplayMontageCompatible, viewModel.CurrentFrame!.HasTemporaryMontage));
        };

        await viewModel.SetTemporaryWaveformMontageAsync(temporary);

        Assert.Same(temporary, viewModel.CurrentViewingMontage);
        Assert.Contains(temporary, viewModel.CompatibleViewingMontages);
        Assert.Same(algorithm, viewModel.WorkspaceMontageSelection.Capture());
        Assert.Equal("在线导联", viewModel.CurrentFrame!.ViewingMontageName);
        Assert.True(viewModel.CurrentFrame.HasTemporaryMontage);
        Assert.False(viewModel.Preprocessing.IsDisplayMontageCompatible);
        Assert.Equal((false, false), transitionFrames[0]);

        await viewModel.SelectViewingMontageAsync(original);
        Assert.Same(original, viewModel.CurrentViewingMontage);
        Assert.Same(algorithm, viewModel.WorkspaceMontageSelection.Capture());
        Assert.False(viewModel.CurrentFrame!.HasTemporaryMontage);
        Assert.True(viewModel.Preprocessing.IsDisplayMontageCompatible);
        Assert.Equal((true, false), transitionFrames[1]);
        await viewModel.SelectViewingMontageAsync(temporary);
        Assert.Same(temporary, viewModel.CurrentViewingMontage);
        Assert.False(viewModel.Preprocessing.IsDisplayMontageCompatible);
        Assert.Same(algorithm, viewModel.WorkspaceMontageSelection.Capture());

        var replacement = temporary with { Name = "新在线配置", Revision = 2 };
        await viewModel.SetTemporaryWaveformMontageAsync(replacement);
        Assert.Same(replacement, viewModel.CurrentViewingMontage);
        Assert.False(viewModel.Preprocessing.IsDisplayMontageCompatible);
        Assert.DoesNotContain(temporary, viewModel.CompatibleViewingMontages);
        Assert.Same(algorithm, viewModel.WorkspaceMontageSelection.Capture());
        await viewModel.DisposeAsync();
        Assert.Null(viewModel.TemporaryWaveformMontage);
    }

    [Fact]
    public async Task TemporaryMontage_SuspendsWhileNewWaveformFrameIsStillLoading()
    {
        var configuration = Configuration();
        var reader = new DeferredNavigationReader(Manifest(configuration));
        await using var vm = new RecordingReviewViewModel(reader, NavigationCatalog(Profile("REF", configuration)));
        await vm.InitializeAsync();
        var selection = vm.WorkspaceMontageSelection.Capture();
        var seek = vm.SeekAsync(50);
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var transition = vm.SetTemporaryWaveformMontageAsync(TemporaryMontageFixture.Create(configuration));
        Assert.False(vm.Preprocessing.IsDisplayMontageCompatible);
        Assert.False(vm.CurrentFrame!.HasTemporaryMontage);
        reader.Release.SetResult();
        await Task.WhenAll(seek, transition);
        Assert.True(vm.CurrentFrame!.HasTemporaryMontage);
        Assert.False(vm.Preprocessing.IsDisplayMontageCompatible);
        Assert.Same(selection, vm.WorkspaceMontageSelection.Capture());
        await vm.SelectViewingMontageAsync(selection.SelectedMontage);
        Assert.True(vm.Preprocessing.IsDisplayMontageCompatible);
    }
}
