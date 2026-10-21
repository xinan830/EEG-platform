using System.Reflection;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

[Collection("OnDemandWindowFeature")]
public sealed class OfficialCombinedStrategyTests
{
    public static TheoryData<string> Algorithms => new()
    {
        "psd", "stft", "rbp", "band_ratio", "peak_frequency", "iapf",
        "theta_beta", "faa", "brainbeat",
    };

    [Theory, MemberData(nameof(Algorithms))]
    public async Task Combined_serializes_same_science_and_keeps_cancelled_background_separate(string id)
    {
        var flag = $"BRAIN_PLATFORM_ENABLE_{id.ToUpperInvariant()}_ON_DEMAND";
        var previous = Environment.GetEnvironmentVariable(flag);
        Environment.SetEnvironmentVariable(flag, "1");
        var client = new OnDemandDetailClient
        {
            Respond = request => OnDemandDetailClient.RbpResponse(request) with
            {
                Range = new DynamicWindowRange(request.CursorSample, request.CursorSample + 1,
                    request.CursorSample / 200d, (request.CursorSample + 1) / 200d),
            },
            RespondFullRun = request => new AnalysisRunResponse(
                "background-full", request.RecordingId, request.AnalysisType, "cancelled", "fixture-v1", null, null,
                null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, 10, "cancelled", "已取消"),
        };
        var catalog = OnDemandDetailClient.Catalog(client, id,
            primaryChannel: id == "brainbeat" ? "Fz" : "F3",
            secondaryChannel: id == "brainbeat" ? "Pz" : "F4");
        try
        {
            catalog.SelectedDynamicExecutionMode = "按需预览 + 后台全量";
            await OnDemandDetailClient.Run(catalog);
            var previewRequest = Assert.Single(client.Requests);
            var fullRequest = Assert.Single(client.FullRunRequests);
            Assert.Equal(id, previewRequest.AlgorithmId);
            Assert.Equal(previewRequest.Config.GetRawText(), fullRequest.Config.GetRawText());
            Assert.Equal(id, fullRequest.Config.GetProperty("algorithm_id").GetString());
            Assert.Equal("dynamic", fullRequest.Config.GetProperty("mode").GetString());
            Assert.Equal(id == "brainbeat" ? "Fz" : "F3", fullRequest.Config.GetProperty("channel").GetString());
            if (id == "faa") Assert.Equal("F4", fullRequest.Config.GetProperty("f4_channel").GetString());
            if (id == "brainbeat") Assert.Equal("Pz", fullRequest.Config.GetProperty("secondary_channel").GetString());
            Assert.StartsWith("offline-full:", fullRequest.IdempotencyKey);
            Assert.Equal("cancelled", catalog.BackgroundFullRun!.Status);
            Assert.Null(catalog.LastRun);
            var preview = Assert.IsType<DynamicWindowResponse>(catalog.DisplayedAnalysisWindow);
            Assert.Equal("insufficient_data", preview.State);
            Assert.Null(preview.Result);

            typeof(AlgorithmListViewModel).GetProperty(nameof(AlgorithmListViewModel.BackgroundFullRun))!
                .SetValue(catalog, catalog.BackgroundFullRun with { Status = "running" });
            await (Task)typeof(AlgorithmListViewModel)
                .GetMethod("CancelBackgroundFullRunAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(catalog, null)!;
            Assert.Equal(1, client.CancelledRuns);
            Assert.Same(preview, catalog.DisplayedAnalysisWindow);
        }
        finally
        {
            await catalog.ReleaseWindowAnalysisAsync();
            Environment.SetEnvironmentVariable(flag, previous);
        }
    }
}
