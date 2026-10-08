using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class PsdPreviewLoadingTests
{
    [Fact]
    public async Task PlaybackAndSlider_OnlyProjectLoadedResultsWithoutSubmittingRuns()
    {
        var client = new PreviewClient(Preview(dynamic: true));
        var catalog = new AlgorithmListViewModel(client, new OperationNotificationCenter());
        catalog.SelectedAnalysisMode = "动态";
        await catalog.LoadStructuredPreviewAsync("run", dynamic: true);
        // Without a registered recording, duration is the last returned window (149 s).
        catalog.DynamicTimeProgressPercent = 17 / 149.0 * 100;
        Assert.Equal(17, catalog.DynamicPreviewCursorSeconds, precision: 8);
        Assert.Equal(14e12, catalog.PsdPreviewPoints[0]!.Value);
        catalog.StartDynamicPreviewCommand.Execute(null);
        Assert.True(catalog.IsDynamicPreviewPlaying);
        catalog.PauseDynamicPreviewCommand.Execute(null);
        Assert.False(catalog.IsDynamicPreviewPlaying);
        Assert.Equal(17, catalog.DynamicPreviewCursorSeconds, precision: 8);
        catalog.StepDynamicPreviewCommand.Execute(null);
        Assert.Equal(18, catalog.DynamicPreviewCursorSeconds, precision: 8);
        Assert.False(catalog.HasPsdPreview);
        catalog.DynamicTimeProgressPercent = 19 / 149.0 * 100;
        Assert.True(catalog.HasPsdPreview);
        // PreviewClient throws if any create/poll API is called.
    }

    [Fact]
    public async Task StaticPreview_ExposesBackendBandShares()
    {
        var client = new PreviewClient(Preview(dynamic: false));
        var catalog = new AlgorithmListViewModel(client, new OperationNotificationCenter());

        await catalog.LoadStructuredPreviewAsync("run");

        Assert.True(catalog.HasPsdPreview);
        Assert.Equal(197, catalog.PsdPreviewPoints.Count);
        Assert.Equal(5, catalog.PsdBandSharePoints.Count);
        Assert.Equal(0.6, catalog.PsdBandSharePoints[2].Share);
        Assert.All(catalog.PsdBandSharePoints, point => Assert.True(point.IsAvailable));
    }

    [Fact]
    public async Task LongDynamicPreview_LoadsSpectrumBandSharesAndSeekableWindows()
    {
        var client = new PreviewClient(Preview(dynamic: true));
        var catalog = new AlgorithmListViewModel(client, new OperationNotificationCenter());
        catalog.SelectedAnalysisMode = "动态";

        await catalog.LoadStructuredPreviewAsync("run", dynamic: true);

        Assert.Equal(1_000_000, client.RequestedCells);
        Assert.Equal(146, catalog.DynamicWindowRows.Count);
        Assert.True(catalog.HasPsdPreview);
        Assert.Equal(5, catalog.PsdBandSharePoints.Count);
        Assert.True(catalog.StartDynamicPreviewCommand.CanExecute(null));
        catalog.DynamicPreviewCursorSecondsForTest(17);
        Assert.Equal(17, catalog.DynamicPreviewCursorSeconds);
        Assert.Equal(0.5, catalog.PsdBandSharePoints[2].Share);
        Assert.Equal(14e12, catalog.PsdPreviewPoints[0]!.Value);

        catalog.DynamicPreviewCursorSecondsForTest(18);
        Assert.False(catalog.HasPsdPreview);
        Assert.Empty(catalog.PsdBandSharePoints);
        catalog.DynamicPreviewCursorSecondsForTest(19);
        Assert.True(catalog.HasPsdPreview);
        Assert.Equal(5, catalog.PsdBandSharePoints.Count);
    }

    private static StructuredPreviewResponse Preview(bool dynamic)
    {
        var frequencies = Enumerable.Range(0, 197).Select(index => 1.0 + index * 0.25).ToArray();
        var rows = Enumerable.Range(0, 146).Select(index =>
            Enumerable.Repeat(index + 1.0, 197).ToArray()).ToArray();
        var shares = Enumerable.Range(0, 146).Select(index =>
            index == 13 ? new[] { 0.2, 0.1, 0.5, 0.15, 0.05 } : new[] { 0.1, 0.1, 0.6, 0.15, 0.05 }).ToArray();
        var windows = dynamic ? Enumerable.Range(0, 146).Select(index => JsonSerializer.SerializeToElement(new
        {
            start_s = Math.Max(0, index - 1), end_s = index + 4,
            state = index == 0 ? "Partial" : index == 14 ? "Rejected" : "Complete",
        })).ToArray() : [];
        return new StructuredPreviewResponse("run", new RunArtifact("a", "run", "psd", "psd.npz",
            "application/x-npz", 1, "hash", null, new Dictionary<string, int[]>(), DateTimeOffset.UtcNow),
            JsonSerializer.SerializeToElement(new { kind = "frequency_series" }), ["O1"],
            new TimeRange(0, 149.982), new TimeRange(0, 149.982),
            new Dictionary<string, JsonElement> { ["frequency_hz"] = JsonSerializer.SerializeToElement(frequencies) },
            new Dictionary<string, JsonElement> { ["frequency_hz"] = JsonSerializer.SerializeToElement(new { unit = "Hz" }) },
            new Dictionary<string, JsonElement>
            {
                ["psd"] = dynamic ? JsonSerializer.SerializeToElement(rows) : JsonSerializer.SerializeToElement(rows[0]),
                ["band_share"] = dynamic ? JsonSerializer.SerializeToElement(shares) : JsonSerializer.SerializeToElement(shares[0]),
            },
            new Dictionary<string, JsonElement> { ["psd"] = JsonSerializer.SerializeToElement(new { unit = "V^2/Hz" }) },
            windows, new Dictionary<string, int>(), null, "offline-spectral-v3", "psd-runtime-v2-band-share");
    }

    private sealed class PreviewClient(StructuredPreviewResponse preview) : IAlgorithmClient
    {
        public int RequestedCells { get; private set; }
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken token)
        {
            RequestedCells = maxCells;
            var count = preview.Arrays.Values.Sum(value => value[0].ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Sum(row => row.GetArrayLength()) : value.GetArrayLength());
            if (count > maxCells) throw new AlgorithmApiException(System.Net.HttpStatusCode.Conflict,
                "STRUCTURED_PREVIEW_UNAVAILABLE", "structured preview exceeds the requested cell limit");
            return Task.FromResult(preview);
        }
        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken token) => throw new NotSupportedException();
    }
}
