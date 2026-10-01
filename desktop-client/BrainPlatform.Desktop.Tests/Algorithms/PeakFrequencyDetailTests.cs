using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.PeakFrequency;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;
using BrainPlatform.Desktop.Modules.Algorithms.Views.PeakFrequency;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.RenderableSeries;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class PeakFrequencyDetailTests
{
    [Fact]
    public void StaticResult_UsesBackendFrequencyAndPeakPower()
    {
        var preview = PeakFrequencyResultPreview.Parse(Json("""
            {"metric":{"official":{"algorithm_id":"peak_frequency","peak_frequency_evidence":{"selected_bin_hz":10.5,"selected_power_v2_per_hz":2e-12}},"output":{"unit":"Hz","value":10.5},"channel":"O1","actual_range":{"start_s":2,"end_s":20},"quality":{"status":"clean"}}}
            """));
        Assert.NotNull(preview);
        Assert.Equal("O1", preview.Channel);
        Assert.Equal(10.5, preview.StaticPoint?.ValueHz);
        Assert.Equal(2e-12, preview.StaticPoint?.PeakPowerV2PerHz);
        Assert.Equal(2, preview.StaticPoint?.StartSeconds);
        Assert.Null(PeakFrequencyResultPreview.Parse(Json("""{"metric":{"official":{"algorithm_id":"iapf"},"output":{"unit":"Hz","value":10}}}""")));
    }

    [Fact]
    public void DynamicResult_UsesCursorAndLeavesRejectedWindowEmpty()
    {
        var preview = Assert.IsType<PeakFrequencyResultPreview>(PeakFrequencyResultPreview.Parse(DynamicSummary()));
        Assert.Null(preview.CurrentAt(3));
        Assert.Equal(10, preview.CurrentAt(10)?.ValueHz);
        Assert.Null(preview.CurrentAt(11)?.ValueHz);
        Assert.Equal("当前窗口未通过 PSD 质量门", preview.CurrentAt(11)?.Failure);
        Assert.Equal(11, preview.CurrentAt(12)?.ValueHz);
        Assert.Equal([10.0, 11.0], preview.VisibleThrough(11).Select(point => point.EndSeconds));
    }

    [Fact]
    public void Detail_ConnectsSharedTimelineAndQualityCard()
    {
        var catalog = new AlgorithmListViewModel(new UnusedClient(), new OperationNotificationCenter());
        catalog.SelectedAlgorithm = Algorithm();
        catalog.SelectedAnalysisMode = "动态";
        var detail = new AlgorithmDetailViewModel(catalog);
        Assert.True(detail.Quality.IsAvailable);

        var run = Run(DynamicSummary());
        typeof(AlgorithmListViewModel).GetProperty(nameof(AlgorithmListViewModel.LastRun))!.SetValue(catalog, run);
        PeakFrequencyRunResultHandler.ApplyDynamicWindows(catalog, run);
        Assert.Equal(10, catalog.DynamicPreviewCursorSeconds);
        Assert.Equal("10 Hz", detail.PeakFrequency.ValueText);
        Assert.Equal(0, detail.Quality.RejectedWindowCount);

        catalog.DynamicPreviewCursorSecondsForTest(11);
        Assert.Equal("不可用", detail.PeakFrequency.ValueText);
        Assert.Equal(1, detail.Quality.RejectedWindowCount);
        Assert.Contains("质量门", detail.Quality.FailureReasonText);
        detail.Quality.ToggleFailureDetailsCommand.Execute(null);
        Assert.Contains("1–11 s", detail.Quality.FailureDetailsText);
    }

    [Fact]
    public void TrendChart_RendersBackendValuesAndRejectedGap()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var preview = Assert.IsType<PeakFrequencyResultPreview>(PeakFrequencyResultPreview.Parse(DynamicSummary()));
                var chart = new PeakFrequencyTrendChart { StartSeconds = 0, EndSeconds = 12, Points = preview.DynamicPoints };
                var surface = Assert.IsType<SciChartSurface>(((System.Windows.Controls.Grid)chart.Content).Children[0]);
                var series = Assert.IsType<FastLineRenderableSeries>(Assert.Single(surface.RenderableSeries));
                var values = Assert.IsType<XyDataSeries<double, double>>(series.DataSeries);
                Assert.Equal(3, values.Count);
                Assert.Equal(10, values.YValues[0]);
                Assert.True(double.IsNaN(values.YValues[1]));
                Assert.Equal(11, values.YValues[2]);
                chart.Points = Array.Empty<PeakFrequencyResultPoint>();
                Assert.Empty(values.XValues);
                Assert.Equal(Visibility.Visible, chart.EmptyState.Visibility);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static AlgorithmCatalogItem Algorithm() => new("official", "peak_frequency", "official-peak-frequency-v1", "峰频率", "Peak Frequency", "", [], ["static", "dynamic"], default,
        new DynamicAnalysisPolicy(4, [10], 10, 1, true), "available", true, null, null, null);

    private static JsonElement DynamicSummary() => Json("""
        {"metric":{"official":{"algorithm_id":"peak_frequency"},"output":{"unit":"Hz"},"channel":"O1","series":[
          {"time_s":10,"window_start_s":0,"window_end_s":10,"value":10,"analysis_state":"Complete","quality":{"status":"clean"},"official":{"peak_frequency_evidence":{"selected_power_v2_per_hz":2e-12}}},
          {"time_s":11,"window_start_s":1,"window_end_s":11,"value":null,"analysis_state":"Rejected","quality":{"status":"gate_failed","reasons":["PSD_QUALITY_GATE_FAILED"]},"failure":{"code":"PSD_QUALITY_GATE_FAILED","message":"当前窗口未通过 PSD 质量门"}},
          {"time_s":12,"window_start_s":2,"window_end_s":12,"value":11,"analysis_state":"Complete","quality":{"status":"clean"}}
        ]}}
        """);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private static AnalysisRunResponse Run(JsonElement summary) => new("run-peak", "recording-1", "official_algorithm", "completed",
        "official-peak-frequency-v1", new TimeRange(0, 12), new TimeRange(0, 12), summary, null, null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class UnusedClient : IAlgorithmClient
    {
        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
