using System.Net;
using System.Net.Http;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;
using BrainPlatform.Desktop.Modules.Review.ViewModels;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Theory]
    [InlineData(false, "F3", false)]
    [InlineData(true, "F3", false)]
    [InlineData(true, "F3-REF", true)]
    public async Task Scientific_comparison_requires_registered_montage_and_matching_display_rows(
        bool montageRegistered, string responseChannel, bool expectedReady)
    {
        var configuration = Configuration();
        var manifest = Manifest(configuration);
        var profile = Profile("selected-montage", configuration);
        var descriptor = new HistoricalMontageDescriptor(profile.Id, "r1", "fp", "Saved", true,
            true, null, [new("F3-REF", "F3-REF", "F3", "V", true, null)], profile.Id);
        var recording = new RegisteredRecording(manifest.SessionId.ToString("N"), "imported.edf", ".edf",
            "2026-11-25", 500, 20, ["F3"], "sha", montageRegistered ? [descriptor] : []);
        var catalog = new RecordingMontageCatalogResult(profile, AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(profile)], []);
        var handler = new ScientificComparisonHandler(responseChannel);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var vm = new RecordingReviewViewModel(new FakeReader(manifest), catalog,
            algorithmClient: new HttpAlgorithmClient(http), registeredAnalysisRecording: recording);
        await vm.InitializeAsync();
        vm.Preprocessing.IsOverlayCompareEnabled = true;
        await vm.Preprocessing.RefreshOverlayWindowAsync();

        Assert.Equal(["F3-REF"], vm.CurrentFrame!.OutputChannelNames);
        Assert.Equal(expectedReady, vm.Preprocessing.IsScientificComparisonReady);
        Assert.Equal(expectedReady, vm.Preprocessing.CanDisplayScientificComparison(vm.CurrentFrame.OutputChannelNames));
        Assert.False(vm.Preprocessing.CanDisplayScientificComparison(["F7-REF"]));
        if (expectedReady)
        {
            Assert.NotNull(vm.Preprocessing.CurrentOverlayWindow);
            Assert.Equal(profile.Id, handler.LastMontageId);
            Assert.True(vm.Preprocessing.CanDisplayScientificComparison(["f3-ref"]));
            handler.Channel = "F3";
            await vm.Preprocessing.RefreshOverlayWindowAsync();
            Assert.Null(vm.Preprocessing.CurrentOverlayWindow);
            Assert.False(vm.Preprocessing.CanDisplayScientificComparison(["F3-REF"]));
            Assert.Contains("已恢复普通彩色阅图", vm.Preprocessing.OverlayStatusText);
            handler.Channel = "F3-REF";
            await vm.Preprocessing.RefreshOverlayWindowAsync();
            Assert.True(vm.Preprocessing.IsScientificComparisonReady);
            handler.FailWithResourceLimit = true;
            await vm.Preprocessing.RefreshOverlayWindowAsync();
            Assert.Null(vm.Preprocessing.CurrentOverlayWindow);
            Assert.False(vm.Preprocessing.IsScientificComparisonReady);
            Assert.Contains("缩短", vm.Preprocessing.OverlayStatusText);
            Assert.Contains("已恢复普通彩色阅图", vm.Preprocessing.OverlayStatusText);
        }
        else
        {
            Assert.Null(vm.Preprocessing.CurrentOverlayWindow);
            Assert.Contains("已恢复普通彩色阅图", vm.Preprocessing.OverlayStatusText);
            Assert.Contains(montageRegistered ? "通道" : "导联快照", vm.Preprocessing.OverlayStatusText);
        }
        if (!montageRegistered) Assert.Equal(0, handler.RequestCount);
    }

    private sealed class ScientificComparisonHandler(string channel) : HttpMessageHandler
    {
        public string Channel { get; set; } = channel;
        public bool FailWithResourceLimit { get; set; }
        public int RequestCount { get; private set; }
        public string? LastMontageId { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            RequestCount++;
            if (FailWithResourceLimit)
                return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        code = "PREPROCESSING_PREVIEW_LIMIT_EXCEEDED",
                        message = "科学对比超出资源上限，请缩短时间窗。", request_id = "limit-test",
                    }), System.Text.Encoding.UTF8, "application/json"),
                };
            var channel = Channel;
            var input = JsonSerializer.Deserialize<PreprocessingPreviewWindowRequest>(await request.Content!.ReadAsStringAsync(token))!;
            LastMontageId = input.MontageId;
            var times = new Dictionary<string, IReadOnlyList<double>> { [channel] = [input.StartSeconds, input.StartSeconds + 0.01] };
            var values = new Dictionary<string, IReadOnlyList<double?>> { [channel] = [1, 2] };
            var output = new PreprocessingPreviewWindowResponse(
                PreprocessingPreviewWindowResponse.CurrentSchemaVersion, request.RequestUri!.AbsolutePath.Split('/')[3],
                "sha", input.MontageId, "view", (long)(input.StartSeconds * 500),
                (long)((input.StartSeconds + input.WindowSeconds) * 500), input.StartSeconds, input.StartSeconds + input.WindowSeconds,
                500, "µV", new("whole_recording_before_window_slice", "zero_phase", [1, 50], 50, 30, "filter"), false,
                new(PreprocessingPreviewWindowResponse.PairedCompressionMethod, false, input.MaxPoints, true),
                new("scientific_baseline", [channel], times, values),
                new("scientific_processed", [channel], [], [], [], times, values, false, "skipped", null));
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(output), System.Text.Encoding.UTF8, "application/json") };
        }
    }
}

public sealed partial class ReviewPreprocessingTests
{
    [Fact]
    public async Task Exclusion_badge_uses_exact_output_dependencies_including_bipolar_negative_input()
    {
        using var vm = CreateViewModel(new FakePreprocessingClient(), out _);
        await vm.InspectBadChannelsAsync();
        vm.BadChannelPolicy = "exclude";
        var preview = await new FakePreprocessingClient().PreviewPreprocessedWindowAsync(
            "rec", new(0, 1, "montage", 1, 50, 50, 1200, vm.BuildPayload(true)), CancellationToken.None);
        preview = preview with { Processed = preview.Processed with { ExcludedOutputChannels = ["F4-C4", "C4-P4"] } };
        foreach (var name in new[] { "F4-C4", "c4-p4" })
        {
            var badge = ReviewPreprocessingProjection.ResolveChannelBadge(name, vm.BadChannelRows, "exclude", preview);
            Assert.Equal(ChannelVisualPreprocessingState.Excluded, badge.State);
            Assert.Contains("浅灰", badge.Tooltip);
        }
        Assert.Equal(ChannelVisualPreprocessingState.Normal,
            ReviewPreprocessingProjection.ResolveChannelBadge("F4-P4", vm.BadChannelRows, "exclude", preview).State);
        Assert.NotEqual(ChannelVisualPreprocessingState.Excluded,
            ReviewPreprocessingProjection.ResolveChannelBadge("F4-C4", vm.BadChannelRows, "exclude", null).State);
    }
}
