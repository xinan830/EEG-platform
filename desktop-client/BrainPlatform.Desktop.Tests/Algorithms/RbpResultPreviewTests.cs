using System.Text.Json;
using System.Runtime.ExceptionServices;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Rbp;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.RenderableSeries;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class RbpResultPreviewTests
{
    [Fact]
    public void Static_UsesBackendSharesInDeclaredBandOrder()
    {
        var preview = RbpResultPreview.Parse(Run("""
            {"metric":{"quality":{"status":"clean"},"band_values":{"beta":0.4,"alpha":0.3,"theta":0.2,"delta":0.1}}}
            """));

        Assert.Equal([0.1, 0.2, 0.3, 0.4], preview.BandsAt(0).Select(band => band.Share));
        Assert.Equal("10.0%", preview.BandsAt(0)[0].ShareText);
        Assert.False(preview.IsDynamic);
    }

    [Fact]
    public void CurrentVersion_UsesFiveBackendSharesIncludingGamma()
    {
        var preview = RbpResultPreview.Parse(Run("""
            {"metric":{"quality":{"status":"clean"},"band_values":{"gamma":0.3,"beta":0.2,"alpha":0.2,"theta":0.2,"delta":0.1}}}
            """, "official-rbp-v2"));

        Assert.Equal(5, preview.BandsAt(0).Count);
        Assert.Equal([0.1, 0.2, 0.2, 0.2, 0.3], preview.BandsAt(0).Select(band => band.Share));
        Assert.Equal("30–50 Hz", preview.BandsAt(0)[4].Range);
        Assert.Equal("30.0%", preview.BandsAt(0)[4].ShareText);
    }

    [Fact]
    public void CurrentVersion_MissingGammaDoesNotDisplayFourBandSharesAsFiveBandResult()
    {
        var preview = RbpResultPreview.Parse(Run("""
            {"metric":{"quality":{"status":"clean"},"band_values":{"delta":0.1,"theta":0.2,"alpha":0.3,"beta":0.4}}}
            """, "official-rbp-v2"));

        Assert.All(preview.BandsAt(0), band => Assert.Null(band.Share));
    }

    [Fact]
    public void CurrentVersion_InvalidGammaMakesTheFiveBandMeasurementUnavailableAsASet()
    {
        var preview = RbpResultPreview.Parse(Run("""
            {"metric":{"quality":{"status":"clean"},"band_values":{"delta":0.1,"theta":0.2,"alpha":0.3,"beta":0.4,"gamma":1.1}}}
            """, "official-rbp-v2"));

        Assert.All(preview.BandsAt(0), band => Assert.Null(band.Share));
    }

    [Fact]
    public void Dynamic_RespectsCursorAndPreservesRejectedWindowAsGap()
    {
        var preview = RbpResultPreview.Parse(Run("""
            {"metric":{"series":[
              {"time_s":4,"window_start_s":0,"window_end_s":4,"analysis_state":"Partial","quality":{"status":"clean"},"band_values":{"delta":0.1,"theta":0.2,"alpha":0.3,"beta":0.4}},
              {"time_s":5,"window_start_s":1,"window_end_s":5,"analysis_state":"Rejected","quality":{"status":"gate_failed","reasons":["PSD_QUALITY_GATE_FAILED"]},"band_values":{"delta":null,"theta":null,"alpha":null,"beta":null}},
              {"time_s":6,"window_start_s":2,"window_end_s":6,"analysis_state":"Complete","quality":{"status":"clean"},"band_values":{"delta":0.2,"theta":0.2,"alpha":0.2,"beta":0.4}}
            ]}}
            """));

        Assert.True(preview.IsDynamic);
        Assert.All(preview.BandsAt(3), band => Assert.Null(band.Share));
        Assert.Equal(0.1, preview.BandsAt(4)[0].Share);
        Assert.All(preview.BandsAt(5), band => Assert.Null(band.Share));
        Assert.Equal("PSD_QUALITY_GATE_FAILED", preview.Windows[1].Failure);
        Assert.Equal(0.2, preview.BandsAt(6)[0].Share);
    }

    [Fact]
    public void InvalidOrRejectedShares_NeverBecomeZero()
    {
        var rejected = RbpResultPreview.Parse(Run("""
            {"metric":{"quality":{"status":"gate_failed"},"band_values":{"delta":0.5,"theta":0.5,"alpha":0,"beta":0}}}
            """));
        var missing = RbpResultPreview.Parse(Run("""
            {"metric":{"quality":{"status":"clean"},"band_values":{"delta":null,"theta":1.2,"alpha":-0.1,"beta":0.4}}}
            """));

        Assert.All(rejected.BandsAt(0), band => Assert.Null(band.Share));
        Assert.Null(missing.BandsAt(0)[0].Share);
        Assert.Null(missing.BandsAt(0)[1].Share);
        Assert.Null(missing.BandsAt(0)[2].Share);
        Assert.Equal(0.4, missing.BandsAt(0)[3].Share);
    }

    [Fact]
    public void DynamicWindows_UseSharedCursorAndKeepRejectedWindow()
    {
        var catalog = new AlgorithmListViewModel(new UnusedAlgorithmClient(), new OperationNotificationCenter());
        catalog.SelectedAnalysisMode = "动态";
        RbpRunResultHandler.ApplyDynamicWindows(catalog, DynamicRun());

        Assert.Equal(6, catalog.DynamicPreviewCursorSeconds);
        Assert.Equal(3, catalog.DynamicPreviewRows.Count);

        catalog.DynamicPreviewCursorSecondsForTest(5);
        Assert.Equal(2, catalog.DynamicPreviewRows.Count);
        Assert.Equal("已拒绝", catalog.DynamicPreviewRows[^1].State);
    }

    [Fact]
    public void SciChartTrend_LeavesRejectedWindowAsNanGap()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var preview = RbpResultPreview.Parse(DynamicRun());
                var chart = new RbpChart { IsTrend = true, Bands = preview.BandsAt(6), Windows = preview.Windows };
                var surface = Assert.IsType<SciChartSurface>(chart.Content);
                Assert.Equal(4, surface.RenderableSeries.Count);
                var firstLine = Assert.IsType<FastLineRenderableSeries>(surface.RenderableSeries[0]);
                var values = Assert.IsType<XyDataSeries<double, double>>(firstLine.DataSeries);
                Assert.Equal(0.1, values.YValues[0]);
                Assert.True(double.IsNaN(values.YValues[1]));
                Assert.Equal(0.2, values.YValues[2]);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void CurrentVersion_SciChartTrendContainsFiveSeriesIncludingGamma()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var run = Run("""
                    {"metric":{"series":[
                      {"time_s":5,"window_start_s":0,"window_end_s":5,"analysis_state":"Complete","quality":{"status":"clean"},"band_values":{"delta":0.1,"theta":0.2,"alpha":0.2,"beta":0.2,"gamma":0.3}}
                    ]}}
                    """, "official-rbp-v2");
                var preview = RbpResultPreview.Parse(run);
                var chart = new RbpChart { IsTrend = true, Bands = preview.BandsAt(5), Windows = preview.Windows };
                var surface = Assert.IsType<SciChartSurface>(chart.Content);
                Assert.Equal(5, surface.RenderableSeries.Count);
                Assert.Equal("γ Gamma", Assert.IsType<FastLineRenderableSeries>(surface.RenderableSeries[4]).DataSeries.SeriesName);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static AnalysisRunResponse DynamicRun() => Run("""
        {"metric":{"series":[
          {"time_s":4,"window_start_s":0,"window_end_s":4,"analysis_state":"Partial","quality":{"status":"clean"},"band_values":{"delta":0.1,"theta":0.2,"alpha":0.3,"beta":0.4}},
          {"time_s":5,"window_start_s":1,"window_end_s":5,"analysis_state":"Rejected","quality":{"status":"gate_failed","reasons":["RBP_WINDOW_QUALITY_GATE_FAILED"]},"band_values":{"delta":null,"theta":null,"alpha":null,"beta":null}},
          {"time_s":6,"window_start_s":2,"window_end_s":6,"analysis_state":"Complete","quality":{"status":"clean"},"band_values":{"delta":0.2,"theta":0.2,"alpha":0.2,"beta":0.4}}
        ]}}
        """);

    private static AnalysisRunResponse Run(string summary, string scientificVersion = "offline-spectral-v3") => new(
        "run-1", "recording-1", "official_algorithm", "completed", scientificVersion,
        new TimeRange(0, 20), new TimeRange(0, 20), JsonSerializer.Deserialize<JsonElement>(summary),
        null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class UnusedAlgorithmClient : IAlgorithmClient
    {
        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
