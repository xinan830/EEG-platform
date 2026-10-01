using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Iapf;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Iapf;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.RenderableSeries;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class IapfDetailTests
{
    [Fact]
    public void StaticResult_UsesBackendValueAndMethodWithoutInventingUnavailableValue()
    {
        var valid = IapfResultPreview.Parse(Json("""
            {"metric":{"official":{"algorithm_id":"iapf","iapf_evidence":{"source":"cog","peak_hz":9.5,"cog_hz":10.2,"model_r2":0.86}},"output":{"unit":"Hz","value":10.2},"channel":"O1","actual_range":{"start_s":2,"end_s":20},"quality":{"status":"clean"}}}
            """));
        Assert.NotNull(valid);
        Assert.Equal("O1", valid.Channel);
        Assert.Equal(10.2, valid.StaticPoint?.ValueHz);
        Assert.Equal("cog", valid.StaticPoint?.Source);
        Assert.Equal(0.86, valid.StaticPoint?.ModelR2);

        var unavailable = IapfResultPreview.Parse(Json("""
            {"metric":{"official":{"algorithm_id":"iapf"},"output":{"unit":"Hz","value":null},"quality":{"status":"gate_failed"},"failure":{"code":"IAPF_UNAVAILABLE","message":"无法得到有效 IAPF"}}}
            """));
        Assert.Null(unavailable?.StaticPoint?.ValueHz);
        Assert.Equal("无法得到有效 IAPF", unavailable?.StaticPoint?.Failure);
        Assert.Null(IapfResultPreview.Parse(Json("""{"metric":{"official":{"algorithm_id":"peak_frequency"},"output":{"unit":"Hz","value":10}}}""")));
    }

    [Fact]
    public void DynamicResult_UsesRecordingTimeAndDoesNotCarryValueAcrossRejectedWindow()
    {
        var preview = Assert.IsType<IapfResultPreview>(IapfResultPreview.Parse(DynamicSummary()));

        Assert.Null(preview.CurrentAt(3));
        Assert.Null(preview.CurrentAt(4)?.ValueHz);
        Assert.Equal(10, preview.CurrentAt(10)?.ValueHz);
        Assert.Null(preview.CurrentAt(11)?.ValueHz);
        Assert.Equal("质量门未通过", preview.CurrentAt(11)?.Failure);
        Assert.Equal(11, preview.CurrentAt(12)?.ValueHz);
        Assert.Equal([4.0, 10.0, 11.0], preview.VisibleThrough(11).Select(point => point.TimeSeconds));
        Assert.False(preview.DynamicPoints[0].IsComplete);
        Assert.False(preview.DynamicPoints[2].IsComplete);
    }

    [Fact]
    public void DynamicTimeline_ReleasesIapfWindowsUsingSharedCursor()
    {
        var catalog = new AlgorithmListViewModel(new UnusedClient(), new OperationNotificationCenter());
        catalog.SelectedAlgorithm = new AlgorithmCatalogItem("official", "iapf", "official-iapf-v2", "IAPF", "IAPF", "", [], ["static", "dynamic"], default,
            new DynamicAnalysisPolicy(4, [10], 10, 1, true), "available", true, null, null, null);
        catalog.SelectedAnalysisMode = "动态";

        IapfRunResultHandler.ApplyDynamicWindows(catalog, Run(DynamicSummary()));
        Assert.Equal(10, catalog.DynamicPreviewCursorSeconds);
        Assert.Equal(2, catalog.DynamicPreviewRows.Count);

        catalog.DynamicPreviewCursorSecondsForTest(11);
        Assert.Equal(3, catalog.DynamicPreviewRows.Count);
        Assert.Equal("已拒绝", catalog.DynamicPreviewRows[^1].State);
        Assert.Equal("质量门未通过", catalog.DynamicPreviewRows[^1].Failure);
        Assert.Contains("已拒绝：1", new AnalysisContextViewModel(catalog).WindowStateText);
    }

    [Fact]
    public void SharedQualityCard_UsesIapfStatusAndReleasedFailureDetails()
    {
        var catalog = new AlgorithmListViewModel(new UnusedClient(), new OperationNotificationCenter());
        catalog.SelectedAlgorithm = new AlgorithmCatalogItem("official", "iapf", "official-iapf-v2", "IAPF", "IAPF", "", [], ["static", "dynamic"], default,
            new DynamicAnalysisPolicy(4, [10], 10, 1, true), "available", true, null, null, null);
        catalog.SelectedAnalysisMode = "动态";
        var detail = new AlgorithmDetailViewModel(catalog);
        Assert.True(detail.Quality.IsAvailable);
        Assert.Equal("尚未运行分析", detail.Quality.QualityText);

        var run = Run(DynamicSummary());
        typeof(AlgorithmListViewModel).GetProperty(nameof(AlgorithmListViewModel.LastRun), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(catalog, run);
        IapfRunResultHandler.ApplyDynamicWindows(catalog, run);
        Assert.Equal(0, detail.Quality.RejectedWindowCount);

        catalog.DynamicPreviewCursorSecondsForTest(11);
        Assert.Equal(1, detail.Quality.RejectedWindowCount);
        Assert.Equal("未通过质量门", detail.Quality.QualityText);
        Assert.Contains("质量门未通过", detail.Quality.FailureReasonText);
        detail.Quality.ToggleFailureDetailsCommand.Execute(null);
        Assert.True(detail.Quality.IsFailureDetailsExpanded);
        Assert.Contains("1–11 s：质量门未通过", detail.Quality.FailureDetailsText);
    }

    [Fact]
    public void SharedQualityCard_IsVisibleForIapfAndHiddenForUnsupportedAlgorithm()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var catalog = new AlgorithmListViewModel(new UnusedClient(), new OperationNotificationCenter());
                var detail = new AlgorithmDetailViewModel(catalog);
                var card = new QualityStatusCard { DataContext = detail.Quality };
                var window = new Window { Content = card, Width = 900, Height = 300, ShowInTaskbar = false, Opacity = 0 };
                window.Show();
                try
                {
                    catalog.SelectedAlgorithm = new AlgorithmCatalogItem("official", "iapf", "official-iapf-v2", "IAPF", "IAPF", "", [], ["static", "dynamic"], default,
                        new DynamicAnalysisPolicy(4, [10], 10, 1, true), "available", true, null, null, null);
                    Assert.True(detail.Quality.IsAvailable);
                    // Binding transfers can be queued even though the source notification is synchronous.
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    card.UpdateLayout();
                    Assert.Equal(Visibility.Visible, card.Visibility);

                    catalog.SelectedAlgorithm = new AlgorithmCatalogItem("official", "unsupported_algorithm", "unsupported-v1", "Unsupported", "Unsupported", "", [], ["static"], default,
                        new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
                    Assert.False(detail.Quality.IsAvailable);
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    card.UpdateLayout();
                    Assert.Equal(Visibility.Collapsed, card.Visibility);
                }
                finally { window.Close(); }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void TrendChart_KeepsRejectedPointAsGapAndClearsOldSeries()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var preview = Assert.IsType<IapfResultPreview>(IapfResultPreview.Parse(DynamicSummary()));
                var chart = new IapfTrendChart { StartSeconds = 0, EndSeconds = 12, Points = preview.DynamicPoints };
                var surface = Assert.IsType<SciChartSurface>(((System.Windows.Controls.Grid)chart.Content).Children[0]);
                var series = Assert.IsType<FastLineRenderableSeries>(Assert.Single(surface.RenderableSeries));
                var values = Assert.IsType<XyDataSeries<double, double>>(series.DataSeries);

                Assert.Equal(4, values.Count);
                Assert.Equal(10, values.YValues[1]);
                Assert.True(double.IsNaN(values.YValues[2]));
                Assert.Equal(11, values.YValues[3]);
                Assert.Equal(12d, Convert.ToDouble(surface.XAxes[0].VisibleRange.Max));

                chart.Points = Array.Empty<IapfResultPoint>();
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

    private static JsonElement DynamicSummary() => Json("""
        {"metric":{"official":{"algorithm_id":"iapf"},"output":{"unit":"Hz"},"channel":"O1","series":[
          {"time_s":4,"window_start_s":0,"window_end_s":4,"value":9.5,"analysis_state":"Partial","quality":{"status":"clean"}},
          {"time_s":10,"window_start_s":0,"window_end_s":10,"value":10,"analysis_state":"Complete","quality":{"status":"clean"},"official":{"iapf_evidence":{"source":"peak","peak_hz":10,"cog_hz":9.8,"model_r2":0.9}}},
          {"time_s":11,"window_start_s":1,"window_end_s":11,"value":null,"analysis_state":"Rejected","quality":{"status":"gate_failed"},"failure":{"code":"PSD_QUALITY_GATE_FAILED","message":"质量门未通过"}},
          {"time_s":12,"window_start_s":2,"window_end_s":12,"value":11,"analysis_state":"Complete","quality":{"status":"clean"},"official":{"iapf_evidence":{"source":"cog","peak_hz":10.5,"cog_hz":11}}}
        ]}}
        """);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private static AnalysisRunResponse Run(JsonElement summary) => new(
        "run-1", "recording-1", "official_algorithm", "completed", "official-iapf-v2",
        new TimeRange(0, 12), new TimeRange(0, 12), summary, null, null,
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
