using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Theory]
    [InlineData("faa", "F3", "F4")]
    [InlineData("iapf", "F3", null)]
    [InlineData("theta_beta", "F3", null)]
    [InlineData("brainbeat", "Fz", "Pz")]
    public async Task Scalar_algorithm_panel_uses_review_cursor_and_its_declared_channel_roles(
        string algorithmId, string channel, string? secondaryChannel)
    {
        var configuration = Configuration();
        var catalog = new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3", "F4", "Fz", "Pz"]), [], []);
        var provider = new PanelProvider(algorithmId);
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            recordingDirectory: "_temp/scalar-review-fixture", analysisCoordinators: [new ReviewAnalysisCoordinator(provider)]);
        await vm.InitializeAsync();
        await vm.SeekAsync(10);

        await vm.OpenAnalysisAsync(algorithmId);

        var request = Assert.Single(provider.Configurations);
        Assert.Equal(algorithmId, request.GetProperty("algorithm_id").GetString());
        Assert.Equal(channel, request.GetProperty("channel").GetString());
        if (secondaryChannel is not null)
        {
            var field = algorithmId == "faa" ? "f4_channel" : "secondary_channel";
            Assert.Equal(secondaryChannel, request.GetProperty(field).GetString());
        }
        Assert.Equal(10, vm.PositionSeconds);
        Assert.Equal(10, vm.AnalysisPanelStartSeconds);
        Assert.True(vm.IsAnalysisAttached);
    }

    [Fact]
    public async Task Scalar_panel_chart_starts_at_open_cursor_and_keeps_that_origin_during_playback()
    {
        var configuration = Configuration();
        var catalog = new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3"]), [], []);
        var provider = new PanelProvider("iapf");
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            recordingDirectory: "_temp/scalar-review-fixture", analysisCoordinators: [new ReviewAnalysisCoordinator(provider)]);
        await vm.InitializeAsync();
        await vm.SeekAsync(30);

        await vm.OpenAnalysisAsync("iapf");

        Assert.Equal(30, vm.AnalysisPanelStartSeconds);
        await vm.SeekAsync(35);
        Assert.Equal(35, vm.PositionSeconds);
        Assert.Equal(30, vm.AnalysisPanelStartSeconds);
    }

    [Fact]
    public async Task Rbp_panel_chart_starts_at_open_cursor()
    {
        var configuration = Configuration();
        var catalog = new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3"]), [], []);
        var provider = new PanelProvider("rbp");
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            recordingDirectory: "_temp/rbp-review-fixture", analysisCoordinators: [new ReviewAnalysisCoordinator(provider)]);
        await vm.InitializeAsync();
        await vm.SeekAsync(30);

        await vm.OpenAnalysisAsync("rbp");

        Assert.Equal(30, vm.AnalysisPanelStartSeconds);
        Assert.DoesNotContain(vm.RbpAnalysis.Trend, point => point.EndSeconds < 30);
    }

    [Theory]
    [InlineData("faa")]
    [InlineData("iapf")]
    [InlineData("theta_beta")]
    [InlineData("brainbeat")]
    public async Task Closing_scalar_panel_cancels_its_pending_window_without_moving_review_cursor(string algorithmId)
    {
        var configuration = Configuration();
        var catalog = new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3", "F4", "Fz", "Pz"]), [], []);
        var provider = new PanelProvider(algorithmId, deferWindow: true);
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            recordingDirectory: "_temp/scalar-review-fixture", analysisCoordinators: [new ReviewAnalysisCoordinator(provider)]);
        await vm.InitializeAsync();
        await vm.SeekAsync(10);
        var open = vm.OpenAnalysisAsync(algorithmId);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var close = vm.CloseAnalysisAsync();
        Assert.True(provider.PendingToken.IsCancellationRequested);
        provider.Release.TrySetResult();
        await Task.WhenAll(open, close).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(vm.IsAnalysisAttached);
        Assert.False(vm.IsAnalysisPanelOpen);
        Assert.Equal(10, vm.PositionSeconds);
        Assert.Empty(vm.FaaAnalysis.Points);
        Assert.Empty(vm.IapfAnalysis.Points);
        Assert.Empty(vm.ThetaBetaAnalysis.Points);
        Assert.Empty(vm.BrainbeatAnalysis.Points);
    }

    [Fact]
    public async Task Channel_change_clears_image_and_rebinds_at_the_paused_workspace_cursor()
    {
        var configuration = Configuration();
        var catalog = new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3", "F7"]), [], []);
        var provider = new PanelProvider("stft");
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            recordingDirectory: "_temp/stft-review-fixture", analysisCoordinators: [new ReviewAnalysisCoordinator(provider)]);
        await vm.InitializeAsync();
        await vm.SeekAsync(10);
        await vm.OpenAnalysisAsync("stft");
        Assert.NotNull(vm.StftAnalysis.Preview);
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CurrentAnalysisWindow) && vm.StftAnalysis.Preview is not null &&
                provider.Configurations.Count == 2) updated.TrySetResult();
        };

        vm.StftParameters.Channel = "F7";
        Assert.Null(vm.StftAnalysis.Preview);
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["F3", "F7"], provider.Configurations.Select(config => config.GetProperty("channel").GetString()));
        Assert.Equal(10, vm.PositionSeconds);
        Assert.False(vm.IsPlaying);
        Assert.Equal(1, provider.Registrations);
        await vm.CloseAnalysisAsync();
        Assert.Null(vm.StftAnalysis.Preview);
        await vm.OpenAnalysisAsync("stft");
        Assert.NotNull(vm.StftAnalysis.Preview);
        Assert.Equal("F7", provider.Configurations.Last().GetProperty("channel").GetString());
    }

    [Fact]
    public async Task Failed_initial_load_status_is_cleared_when_a_later_window_succeeds()
    {
        var configuration = Configuration();
        var catalog = new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3"]), [], []);
        var provider = new PanelProvider("stft") { FailNextWindow = true };
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            recordingDirectory: "_temp/stft-review-fixture", analysisCoordinators: [new ReviewAnalysisCoordinator(provider)]);
        await vm.InitializeAsync();
        await vm.SeekAsync(10);
        await vm.OpenAnalysisAsync("stft");
        Assert.Contains("fixture transport failure", vm.AnalysisPreparationText);
        Assert.Null(vm.StftAnalysis.Preview);

        await vm.LoadAnalysisAtCurrentPositionAsync();

        Assert.NotNull(vm.StftAnalysis.Preview);
        Assert.Equal("", vm.AnalysisPreparationText);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Switching_or_closing_rejects_late_preparation_and_window(bool duringCatalog, bool close)
    {
        var configuration = Configuration();
        var catalog = new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3"]), [], []);
        var stft = new PanelProvider("stft", duringCatalog, !duringCatalog);
        var rbp = new PanelProvider("rbp");
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            recordingDirectory: "_temp/stft-review-fixture", analysisCoordinators:
            [new ReviewAnalysisCoordinator(rbp), new ReviewAnalysisCoordinator(stft)]);
        await vm.InitializeAsync();
        await vm.SeekAsync(10);
        var frame = vm.CurrentFrame;
        var open = vm.OpenAnalysisAsync("stft");
        await stft.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var replacement = close ? vm.CloseAnalysisAsync() : vm.OpenAnalysisAsync("rbp");
        Assert.True(stft.PendingToken.IsCancellationRequested);
        Assert.Null(vm.StftAnalysis.Preview);
        stft.Release.TrySetResult();
        await Task.WhenAll(open, replacement).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(!close, vm.IsAnalysisPanelOpen);
        Assert.False(vm.IsStftAnalysisPanelOpen);
        Assert.Equal(!close, vm.IsRbpAnalysisPanelOpen);
        Assert.Equal(!close, vm.IsAnalysisAttached);
        Assert.Null(vm.StftAnalysis.Preview);
        Assert.Equal(duringCatalog ? 0 : 1, stft.Loads);
        Assert.Equal(close ? 0 : 1, rbp.Loads);
        Assert.Equal(1, stft.Registrations);
        Assert.Equal(0, rbp.Registrations);
        Assert.Equal(10, vm.PositionSeconds);
        Assert.Same(frame, vm.CurrentFrame);
        Assert.False(vm.IsPlaying);
    }

    private sealed class PanelProvider(string algorithmId, bool deferCatalog = false, bool deferWindow = false) : IReviewAlgorithmAdapter
    {
        public string AlgorithmId => algorithmId;
        public bool SupportsOnDemandWindows => true;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken PendingToken { get; private set; }
        public int Registrations { get; private set; }
        public int Loads { get; private set; }
        public bool FailNextWindow { get; set; }
        public List<JsonElement> Configurations { get; } = [];

        public Task<RegisteredRecording> RegisterAsync(string directory, CancellationToken cancellationToken,
            IReadOnlyList<MontageProfile>? montageProfiles = null)
        {
            Registrations++;
            var channels = algorithmId switch
            {
                "faa" => new[] { "F3", "F4" },
                "brainbeat" => new[] { "Fz", "Pz" },
                _ => new[] { "F3", "F7" },
            };
            return Task.FromResult(new RegisteredRecording("registered", "fixture", ".wpf", "now", 500, 100, channels, "sha"));
        }

        public async Task<AlgorithmCatalogItem> GetAlgorithmAsync(CancellationToken cancellationToken)
        {
            if (deferCatalog) await WaitIgnoringCancellation(cancellationToken);
            var parameters = algorithmId == "psd" ? ReviewPsdAnalysisTests.CatalogParameters() : [];
            return new AlgorithmCatalogItem("official", algorithmId, "fixture-v1", algorithmId, "", "", parameters, ["dynamic"],
                default, new DynamicAnalysisPolicy(4, [5], 5, 1, true),
                "available", true, "definition", "1", "impl");
        }

        public async Task<WindowPreviewResponse> LoadOnDemandAsync(string recordingId, AnalysisRunRequest request,
            long cursorSample, CancellationToken cancellationToken)
        {
            Loads++;
            Configurations.Add(request.Config.Clone());
            if (FailNextWindow)
            {
                FailNextWindow = false;
                throw new InvalidOperationException("fixture transport failure");
            }
            if (deferWindow) await WaitIgnoringCancellation(cancellationToken);
            var canonical = JsonSerializer.Serialize(new
                { scientific_configuration_contract = "scientific-config-v1", config = request.Config, context = new { } });
            var identity = JsonSerializer.SerializeToElement(new
            {
                recording_id = recordingId, source_sha256 = "sha", algorithm_id = algorithmId,
                definition_id = "definition", definition_version = "1", definition_sha256 = "def-sha",
                implementation_build = "build", scientific_version = "fixture-v1", implementation_identity = "impl",
                configuration_sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant(),
                window_policy_version = "policy", start_sample = cursorSample - 2500, end_sample = cursorSample,
            });
            var preview = await StftReviewPresentationTests.Ready();
            return preview with
            {
                RecordingId = recordingId, BackendIdentity = identity, ScientificConfigurationJson = canonical,
                CursorSample = cursorSample, CursorSeconds = cursorSample / 500d, ScientificVersion = "fixture-v1",
                Window = preview.Window! with { StartSample = cursorSample - 2500, EndSample = cursorSample },
            };
        }

        private async Task WaitIgnoringCancellation(CancellationToken token)
        {
            PendingToken = token;
            Started.TrySetResult();
            await Release.Task;
        }

        public Task<AnalysisRunResponse> EnsureRunAsync(AnalysisRunRequest request,
            Action<AnalysisRunResponse>? onStatusChanged, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WindowPreviewResponse> LoadAsync(string recordingId, string runId, long cursorSample,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
