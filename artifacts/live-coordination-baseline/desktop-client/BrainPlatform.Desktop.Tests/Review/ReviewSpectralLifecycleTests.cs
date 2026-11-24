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
        Assert.Equal(10500, vm.CurrentAnalysisWindow!.CursorSample);
        Assert.Equal(1, psd.Cursors.Count(sample => sample == 10500));
        Assert.All(psd.Cursors, sample => Assert.InRange(sample, 10000, 11500));
        Assert.Equal(psd.Cursors, rbp.Cursors);
        vm.TogglePlayback();
        Assert.False(vm.IsPlaying);
        Assert.NotNull(vm.SpectralValues.Rows[0].AbsolutePowerUv2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Custom_range_projection_is_cancelled_on_seek_or_drag_without_blocking_waveform_playback(bool drag)
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

        if (drag)
        {
            vm.BeginTimelineDrag();
            Assert.True(psd.CustomRangeToken.IsCancellationRequested);
            Assert.False(vm.CustomFrequencyInspection.HasBins);
            vm.PreviewSeek(30);
            vm.EndTimelineDrag();
        }
        else await vm.SeekAsync(30).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(30, vm.PositionSeconds);
        Assert.NotNull(vm.CurrentFrame);
        Assert.True(psd.CustomRangeToken.IsCancellationRequested);
        Assert.False(psd.CustomRangeRelease.Task.IsCompleted);
        psd.CustomRangeRelease.TrySetResult();
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

    private static async Task CustomFrequencyReady(RecordingReviewViewModel vm, string windowId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!vm.CustomFrequencyInspection.HasBins ||
               !vm.CustomFrequencyInspection.ProvenanceText.Contains(windowId, StringComparison.Ordinal))
            await Task.Delay(10, timeout.Token);
    }

    private sealed class SpectralProvider(string algorithmId) : IReviewAlgorithmAdapter, IReviewScalarProvider,
        IReviewCustomFrequencyProvider
    {
        public string AlgorithmId => algorithmId;
        public bool SupportsOnDemandWindows => true;
        public List<long> Cursors { get; } = [];
        public List<JsonElement> Configurations { get; } = [];
        public int Registrations { get; private set; }
        public int Projections { get; private set; }
        public int Releases { get; private set; }
        public bool DeferProjection { get; set; }
        public bool DeferCustomRange { get; init; }
        public bool IgnoreCustomCancellation { get; init; }
        public bool DeferCustomBins { get; init; }
        public int DeferredCustomBinsLoad { get; init; } = 1;
        public int CustomBinLoads { get; private set; }
        public int CustomRangeLoads { get; private set; }
        public List<(double Low, double High)> CustomRanges { get; } = [];
        public TaskCompletionSource CustomBinsStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CustomBinsRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken CustomBinsToken { get; private set; }
        public TaskCompletionSource CustomRangeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CustomRangeRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken CustomRangeToken { get; private set; }
        public bool DeferWindow { get; set; }
        public bool AlignWindowToStep { get; init; }
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
            return Window(algorithmId, AlignWindowToStep ? cursorSample / 500 * 500 : cursorSample, request.Config,
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
        public async Task<CustomFrequencyBinsResponse> LoadBinsAsync(WindowPreviewResponse psd,
            WindowPreviewResponse rbp, CancellationToken cancellationToken)
        {
            CustomBinLoads++;
            if (DeferCustomBins && CustomBinLoads == DeferredCustomBinsLoad)
            {
                CustomBinsToken = cancellationToken;
                CustomBinsStarted.TrySetResult();
                await CustomBinsRelease.Task;
            }
            return CustomBins(psd, rbp);
        }

        public async Task<CustomFrequencyRangeResponse> LoadRangeAsync(WindowPreviewResponse psd,
            WindowPreviewResponse rbp, double lowHz, double highHz, CancellationToken cancellationToken)
        {
            CustomRangeLoads++;
            CustomRanges.Add((lowHz, highHz));
            if (DeferCustomRange && CustomRangeLoads == 1)
            {
                CustomRangeToken = cancellationToken;
                CustomRangeStarted.TrySetResult();
                await CustomRangeRelease.Task.WaitAsync(IgnoreCustomCancellation ? CancellationToken.None : cancellationToken);
            }
            if (!IgnoreCustomCancellation) cancellationToken.ThrowIfCancellationRequested();
            var bins = CustomBins(psd, rbp);
            var channels = bins.Channels.Select(row => new CustomFrequencyRangeChannel(row.Channel,
                row.PsdQuality, row.PsdQualityReasons, row.RbpQuality, row.RbpQualityReasons,
                row.RbpDenominatorUv2, 2, 4, 4, 2)).ToArray();
            return new CustomFrequencyRangeResponse("range", psd.RecordingId,
                psd.Window!.StartSample, psd.Window.EndSample, lowHz, highHz, channels,
                bins.PsdProvenance, bins.RbpProvenance);
        }

        private static CustomFrequencyBinsResponse CustomBins(WindowPreviewResponse psd, WindowPreviewResponse rbp)
        {
            var channels = psd.Evidence!.Value.GetProperty("extensions").GetProperty("channel_order")
                .GetProperty("channels").EnumerateArray().Select(item => item.GetString()!).ToArray();
            var psdIdentity = psd.BackendIdentity!.Value;
            var rbpIdentity = rbp.BackendIdentity!.Value;
            static CustomFrequencyProvenance Provenance(JsonElement identity, string windowId) => new(windowId,
                identity.GetProperty("scientific_version").GetString()!,
                identity.GetProperty("implementation_identity").GetString()!,
                identity.GetProperty("configuration_sha256").GetString()!,
                identity.GetProperty("window_policy_version").GetString()!);
            var points = channels.Select((channel, index) => new CustomFrequencyBinChannel(
                channel, "clean", [], "clean", [], index == 0 ? 100 : 200,
                [1, 2, 3, 4], [1, 2, 3, 4], [1, 2, 3, 4], [1, 2, 3, 4])).ToArray();
            return new CustomFrequencyBinsResponse("bins", psd.RecordingId,
                psd.Window!.StartSample, psd.Window.EndSample, [1, 2, 3, 4],
                [new(1, 1.5), new(1.5, 2.5), new(2.5, 3.5), new(3.5, 4)], points,
                Provenance(psdIdentity, psd.Window.WindowId), Provenance(rbpIdentity, rbp.Window!.WindowId));
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
