using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Review.Analysis;
using static BrainPlatform.Desktop.Tests.Review.ReviewSpectralFixture;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Fact]
    public async Task Spectral_scalar_wait_does_not_block_waveform_seek_or_playback_ticks()
    {
        var now = 0d;
        var psd = new SpectralProvider("psd") { DeferProjection = true };
        var rbp = new SpectralProvider("rbp");
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(Configuration()), 100),
            new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
                new RawSignalViewDefinition(ChannelNames), [], []), recordingDirectory: "_temp/spectral-wait-fixture",
            analysisCoordinators: [new ReviewAnalysisCoordinator(psd), new ReviewAnalysisCoordinator(rbp)],
            playbackClockSeconds: () => now);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        await vm.OpenAnalysisAsync("spectral-values");
        await psd.ProjectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await vm.SeekAsync(30).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(30, vm.PositionSeconds);
        Assert.NotNull(vm.CurrentFrame);
        Assert.False(psd.ProjectionRelease.Task.IsCompleted);
        vm.TogglePlayback();
        now = 1.25;
        vm.TickPlayback();
        Assert.Equal(31.25, vm.PositionSeconds);
        Assert.True(psd.ProjectionToken.IsCancellationRequested);
        psd.ProjectionRelease.TrySetResult();
        await SpectralReady(vm, "psd-15500");
    }

    [Fact]
    public async Task Spectral_panel_shares_cursor_and_clears_values_during_drag_and_close()
    {
        var psd = new SpectralProvider("psd");
        var rbp = new SpectralProvider("rbp");
        await using var vm = SpectralReview(psd, rbp);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        await vm.OpenAnalysisAsync("spectral-values");
        await SpectralReady(vm, "psd-10000");
        Assert.True(vm.IsSpectralValuesPanelOpen);
        Assert.False(vm.IsPsdAnalysisPanelOpen);
        Assert.Equal(ChannelNames, vm.SpectralValues.Rows.Select(row => row.Channel));
        Assert.Equal(new long[] { 10000 }, psd.Cursors);
        Assert.Equal(psd.Cursors, rbp.Cursors);
        Assert.Equal(1, psd.Registrations);
        Assert.Equal(0, rbp.Registrations);
        Assert.Equal(ChannelNames, rbp.Configurations[0].GetProperty("channels").EnumerateArray().Select(x => x.GetString()));

        vm.SpectralValues.SelectedBandId = "beta";
        Assert.Single(psd.Cursors);
        Assert.Single(rbp.Cursors);
        Assert.Equal(1, psd.Projections);
        Assert.Equal(1, rbp.Projections);
        vm.BeginTimelineDrag();
        vm.PreviewSeek(25.25);
        Assert.All(vm.SpectralValues.Rows, row => Assert.Null(row.PsdDensityUv2Hz));
        vm.EndTimelineDrag();
        await SpectralReady(vm, "psd-12625");
        Assert.Equal(12625, psd.Cursors.Last());
        Assert.Equal(psd.Cursors.Last(), rbp.Cursors.Last());
        Assert.Equal(25.25, vm.PositionSeconds);

        var psdReleases = psd.Releases;
        var rbpReleases = rbp.Releases;
        await vm.CloseAnalysisAsync();
        Assert.False(vm.IsSpectralValuesPanelOpen);
        Assert.True(psd.Releases > psdReleases);
        Assert.True(rbp.Releases > rbpReleases);
        Assert.All(vm.SpectralValues.Rows, row => Assert.Null(row.AbsolutePowerUv2));
    }

    [Fact]
    public async Task Late_projection_cannot_restore_an_old_window_or_closed_panel()
    {
        var psd = new SpectralProvider("psd");
        var rbp = new SpectralProvider("rbp");
        await using var vm = SpectralReview(psd, rbp);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        psd.DeferProjection = true;
        await vm.OpenAnalysisAsync("spectral-values");
        await psd.ProjectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await vm.SeekAsync(30);
        Assert.True(psd.ProjectionToken.IsCancellationRequested);
        await SpectralReady(vm, "psd-15000");
        psd.ProjectionRelease.TrySetResult();
        await Task.Delay(30);
        Assert.Contains("psd-15000", vm.SpectralValues.ProvenanceText);

        psd.ResetProjectionGate();
        psd.DeferProjection = true;
        await vm.SeekAsync(35);
        await psd.ProjectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await vm.CloseAnalysisAsync();
        Assert.True(psd.ProjectionToken.IsCancellationRequested);
        psd.ProjectionRelease.TrySetResult();
        await Task.Delay(30);
        Assert.All(vm.SpectralValues.Rows, row => Assert.Null(row.PsdDensityUv2Hz));
    }

    [Fact]
    public async Task Closing_while_both_source_windows_are_loading_cancels_both()
    {
        var psd = new SpectralProvider("psd") { DeferWindow = true };
        var rbp = new SpectralProvider("rbp") { DeferWindow = true };
        await using var vm = SpectralReview(psd, rbp);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        var opening = vm.OpenAnalysisAsync("spectral-values");
        await Task.WhenAll(psd.WindowStarted.Task, rbp.WindowStarted.Task).WaitAsync(TimeSpan.FromSeconds(3));

        var closing = vm.CloseAnalysisAsync();
        Assert.True(psd.WindowToken.IsCancellationRequested);
        Assert.True(rbp.WindowToken.IsCancellationRequested);
        psd.WindowRelease.TrySetResult();
        rbp.WindowRelease.TrySetResult();
        await Task.WhenAll(opening, closing).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(vm.IsAnalysisPanelOpen);
        Assert.All(vm.SpectralValues.Rows, row => Assert.Null(row.AbsolutePowerUv2));
    }

    [Fact]
    public async Task Spectral_panel_rebinds_both_sources_on_montage_change_and_rejects_late_projection()
    {
        var configuration = Configuration();
        var acquisition = Profile("采集导联", configuration);
        var derived = new DerivedMontageChannel("F3-AVG", "F3", MontageNegativeKind.Mean, ["F3"], 0);
        var alternate = Profile("平均参考", configuration) with
        {
            DerivedChannels = [derived],
            Fingerprint = MontageProfileFingerprint.Create(ChannelConfigurationFingerprint.Create(configuration), [derived]),
        };
        var catalog = new RecordingMontageCatalogResult(acquisition, AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(acquisition),
                new CompatibleRecordingMontage(alternate)], []);
        var psd = new SpectralProvider("psd") { MontageCatalogue = [Descriptor(acquisition, true), Descriptor(alternate, false)] };
        var rbp = new SpectralProvider("rbp");
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            recordingDirectory: "_temp/spectral-montage-fixture",
            analysisCoordinators: [new ReviewAnalysisCoordinator(psd), new ReviewAnalysisCoordinator(rbp)]);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        psd.DeferProjection = true;
        await vm.OpenAnalysisAsync("spectral-values");
        await psd.ProjectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await vm.SelectViewingMontageAsync(alternate);
        Assert.True(psd.ProjectionToken.IsCancellationRequested);
        Assert.All(vm.SpectralValues.Rows, row => Assert.Null(row.PsdDensityUv2Hz));
        psd.ProjectionRelease.TrySetResult();
        await SpectralReady(vm, "psd-10000");
        Assert.Equal("F3-AVG", Assert.Single(vm.SpectralValues.Rows).Channel);
        Assert.All(new[] { psd, rbp }, provider =>
        {
            Assert.Equal(alternate.Id, provider.Configurations.Last().GetProperty("analysis_montage_id").GetString());
            Assert.Equal(new[] { "F3-AVG" }, provider.Configurations.Last().GetProperty("channels")
                .EnumerateArray().Select(item => item.GetString()));
        });
    }

    [Fact]
    public async Task Spectral_panel_playback_uses_shared_quantized_target_and_pause_keeps_values()
    {
        var now = 0d;
        var psd = new SpectralProvider("psd");
        var rbp = new SpectralProvider("rbp");
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(Configuration()), 100),
            new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
                new RawSignalViewDefinition(ChannelNames), [], []), recordingDirectory: "_temp/spectral-play-fixture",
            analysisCoordinators: [new ReviewAnalysisCoordinator(psd), new ReviewAnalysisCoordinator(rbp)],
            playbackClockSeconds: () => now);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        await vm.OpenAnalysisAsync("spectral-values");
        vm.TogglePlayback();
        now = 1.25;
        vm.TickPlayback();
        await SpectralReady(vm, "psd-10500");
        Assert.Equal(10500, psd.Cursors.Last());
        Assert.Equal(psd.Cursors.Last(), rbp.Cursors.Last());
        vm.TogglePlayback();
        Assert.False(vm.IsPlaying);
        Assert.NotNull(vm.SpectralValues.Rows[0].AbsolutePowerUv2);
    }

    private static RecordingReviewViewModel SpectralReview(SpectralProvider psd, SpectralProvider rbp) => new(
        new FakeReader(Manifest(Configuration()), 100),
        new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(ChannelNames), [], []), recordingDirectory: "_temp/spectral-review-fixture",
        analysisCoordinators: [new ReviewAnalysisCoordinator(psd), new ReviewAnalysisCoordinator(rbp)]);

    private static async Task SpectralReady(RecordingReviewViewModel vm, string windowId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!vm.SpectralValues.ProvenanceText.Contains(windowId, StringComparison.Ordinal) ||
               vm.SpectralValues.Rows.FirstOrDefault()?.PsdDensityUv2Hz is null)
            await Task.Delay(10, timeout.Token);
    }

    private sealed class SpectralProvider(string algorithmId) : IReviewAlgorithmAdapter, IReviewScalarProvider
    {
        public string AlgorithmId => algorithmId;
        public bool SupportsOnDemandWindows => true;
        public List<long> Cursors { get; } = [];
        public List<JsonElement> Configurations { get; } = [];
        public int Registrations { get; private set; }
        public int Projections { get; private set; }
        public int Releases { get; private set; }
        public bool DeferProjection { get; set; }
        public bool DeferWindow { get; init; }
        public IReadOnlyList<HistoricalMontageDescriptor>? MontageCatalogue { get; init; }
        public TaskCompletionSource WindowStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WindowRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ProjectionStarted { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ProjectionRelease { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken ProjectionToken { get; private set; }
        public CancellationToken WindowToken { get; private set; }

        public void ResetProjectionGate()
        {
            ProjectionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ProjectionRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        public Task<RegisteredRecording> RegisterAsync(string directory, CancellationToken cancellationToken,
            IReadOnlyList<MontageProfile>? montageProfiles = null)
        {
            Registrations++;
            return Task.FromResult(new RegisteredRecording("registered", "fixture", ".wpf", "now", 500, 100, ChannelNames, "sha", MontageCatalogue));
        }
        public Task<AlgorithmCatalogItem> GetAlgorithmAsync(CancellationToken cancellationToken) => Task.FromResult(
            new AlgorithmCatalogItem("official", algorithmId, "fixture-v1", algorithmId, "", "",
                algorithmId == "psd" ? ReviewPsdAnalysisTests.CatalogParameters() : [], ["dynamic"], default,
                new DynamicAnalysisPolicy(4, [10], 10, 1, true), "available", true, "definition", "1", "impl"));
        public async Task<WindowPreviewResponse> LoadOnDemandAsync(string recordingId, AnalysisRunRequest request,
            long cursorSample, CancellationToken cancellationToken)
        {
            Cursors.Add(cursorSample);
            Configurations.Add(request.Config.Clone());
            if (DeferWindow)
            {
                WindowToken = cancellationToken;
                WindowStarted.TrySetResult();
                await WindowRelease.Task;
            }
            return Window(algorithmId, cursorSample, request.Config,
                request.Config.GetProperty("channels").EnumerateArray().Select(item => item.GetString()!).ToArray());
        }
        public async Task<ResultScalarsResponse> LoadScalarsAsync(WindowPreviewResponse window,
            CancellationToken cancellationToken, IReadOnlyList<string> fieldIds)
        {
            Projections++;
            if (DeferProjection)
            {
                DeferProjection = false;
                ProjectionToken = cancellationToken;
                ProjectionStarted.TrySetResult();
                await ProjectionRelease.Task;
            }
            var projection = Projection(window, algorithmId);
            Assert.Equal(fieldIds, projection.Fields.Select(field => field.FieldId));
            return projection;
        }
        public Task ReleaseOnDemandAsync(CancellationToken cancellationToken = default)
        { Releases++; return Task.CompletedTask; }
        public Task<AnalysisRunResponse> EnsureRunAsync(AnalysisRunRequest request,
            Action<AnalysisRunResponse>? onStatusChanged, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The numeric panel must use existing dynamic windows.");
        public Task<WindowPreviewResponse> LoadAsync(string recordingId, string runId, long cursorSample,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
