using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;
using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Algorithms;

[Collection("OnDemandWindowFeature")]
public sealed class OfflineAnalysisSelectionTests : IDisposable
{
    private static readonly string[] AlgorithmIds =
        ["psd", "stft", "rbp", "band_ratio", "peak_frequency", "iapf", "theta_beta", "faa", "brainbeat"];
    private readonly Dictionary<string, string?> previous = [];

    public OfflineAnalysisSelectionTests()
    {
        foreach (var id in AlgorithmIds.Append("future"))
        {
            var name = Flag(id);
            previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose()
    {
        foreach (var item in previous) Environment.SetEnvironmentVariable(item.Key, item.Value);
    }

    public static TheoryData<string> Algorithms => new(AlgorithmIds);

    [Theory, MemberData(nameof(Algorithms))]
    public void Dropdown_offers_both_modes_without_commands_and_binds_both_directions(string id) =>
        WpfTestApplication.Run(() =>
        {
            var item = OnDemandDetailClient.Catalog(new OnDemandDetailClient(), id).SelectedAlgorithm;
            var catalog = new AlgorithmListViewModel(new OnDemandDetailClient(), new OperationNotificationCenter())
            {
                SelectedAlgorithm = item,
                SelectedAnalysisMode = "动态",
            };
            var card = new AnalysisParameterCard { DataContext = catalog };
            card.Measure(new Size(420, 1800));
            card.Arrange(new Rect(0, 0, 420, card.DesiredSize.Height));
            card.UpdateLayout();
            var combo = Assert.IsType<ComboBox>(card.FindName("DynamicExecutionModeComboBox"));
            Assert.Equal(Visibility.Visible, combo.Visibility);
            Assert.True(combo.ActualWidth > 0);
            Assert.Equal(["全量分析", "按需分析", "按需预览 + 后台全量"], combo.Items.Cast<string>());
            Assert.Equal("全量分析", combo.SelectedItem);
            combo.SelectedItem = "按需分析";
            Assert.True(catalog.IsOnDemandExecutionModeSelected);
            catalog.SelectedDynamicExecutionMode = "全量分析";
            Assert.Equal("全量分析", combo.SelectedItem);
        });

    [Theory, MemberData(nameof(Algorithms))]
    public void Explicit_disable_removes_on_demand_and_rejects_selection(string id)
    {
        Environment.SetEnvironmentVariable(Flag(id), "0");
        var catalog = OnDemandDetailClient.Catalog(new OnDemandDetailClient(), id);
        Assert.Equal(["全量分析"], catalog.DynamicExecutionModes);
        Assert.Equal("全量分析", catalog.SelectedDynamicExecutionMode);
        Assert.False(catalog.IsOnDemandExecutionModeSelected);
    }

    [Fact]
    public async Task Switching_modes_clears_old_projection_and_routes_execution_without_changing_science()
    {
        var client = new OnDemandDetailClient();
        var catalog = OnDemandDetailClient.Catalog(client);
        var detail = new AlgorithmDetailViewModel(catalog).Rbp;
        await OnDemandDetailClient.Run(catalog);
        catalog.DynamicTimeProgressPercent = 20;
        await catalog.CurrentWindowLoad;
        Assert.True(detail.HasResult);
        var config = client.Requests[^1].Config.GetRawText();
        var count = client.Requests.Count;
        catalog.SelectedDynamicExecutionMode = "全量分析";
        Assert.False(catalog.IsOnDemandAnalysis);
        Assert.Null(catalog.DisplayedAnalysisWindow);
        Assert.False(detail.HasResult);
        Assert.Empty(detail.Trend);
        Assert.Null(catalog.LastRun);
        Assert.Equal(count, client.Released);
        await Assert.ThrowsAsync<NotSupportedException>(() => OnDemandDetailClient.Run(catalog));
        Assert.Equal(1, client.FullRunCalls);
        Assert.Equal(count, client.Requests.Count);
        catalog.SelectedDynamicExecutionMode = "按需分析";
        await OnDemandDetailClient.Run(catalog);
        Assert.Equal(config, client.Requests[^1].Config.GetRawText());
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Combined_strategy_starts_preview_then_background_full_with_stable_identity()
    {
        var client = new OnDemandDetailClient();
        var catalog = OnDemandDetailClient.Catalog(client);
        client.RespondFullRun = request => new AnalysisRunResponse(
            "full-run-1", request.RecordingId, request.AnalysisType, "completed", "fixture-v1", null, null,
            null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 10, 10, "completed", "已完成");
        catalog.SelectedDynamicExecutionMode = "按需预览 + 后台全量";

        await OnDemandDetailClient.Run(catalog);

        Assert.Equal(1, client.Requests.Count);
        Assert.Equal(1, client.FullRunCalls);
        var fullRequest = Assert.Single(client.FullRunRequests);
        Assert.StartsWith("offline-full:", fullRequest.IdempotencyKey);
        Assert.Equal(client.Requests[0].Config.GetRawText(), fullRequest.Config.GetRawText());
        Assert.Null(catalog.LastRun);
        Assert.NotNull(catalog.DisplayedAnalysisWindow);
        Assert.NotNull(catalog.BackgroundFullRun);
        Assert.Equal("completed", catalog.BackgroundFullRun!.Status);
        Assert.True(catalog.BackgroundFullRunProgressPercent >= 100);

        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Combined_psd_serves_completed_full_window_for_current_cursor()
    {
        var client = new OnDemandDetailClient
        {
            Respond = OnDemandDetailClient.PsdResponse,
            RespondFullRun = request => new AnalysisRunResponse(
                "full-run-1", request.RecordingId, request.AnalysisType, "completed", "fixture-v1", null, null,
                null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 10, 10,
                "completed", "已完成"),
        };
        var catalog = OnDemandDetailClient.Catalog(client, "psd", "V^2/Hz");
        catalog.SelectedDynamicExecutionMode = "按需预览 + 后台全量";

        await OnDemandDetailClient.Run(catalog);
        await catalog.CurrentWindowLoad;
        var requestCountAfterFullCache = client.Requests.Count;
        catalog.DynamicTimeProgressPercent = 50;
        await catalog.CurrentWindowLoad;

        Assert.Equal(1, client.StructuredPreviewCalls);
        Assert.Equal(0, client.FullWindowPreviewCalls);
        Assert.Equal(requestCountAfterFullCache, client.Requests.Count);
        Assert.Equal("全量已完成窗口（本地缓存）", catalog.DynamicPreviewSourceText);
        Assert.StartsWith("full-cache:", catalog.DisplayedAnalysisWindow!.SessionId);
        Assert.Equal([9e12, 8e12, 7e12],
            catalog.PsdPreviewState.Points.Select(point => point!.Value).ToArray());
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Combined_stft_serves_completed_full_matrix_with_window_provenance()
    {
        var client = new OnDemandDetailClient
        {
            Respond = OnDemandDetailClient.StftResponse,
            RespondFullRun = request => new AnalysisRunResponse(
                "full-run-1", request.RecordingId, request.AnalysisType, "completed", "fixture-v1", null, null,
                null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 10, 10,
                "completed", "已完成"),
            RespondFullStructuredPreview = _ => OnDemandDetailClient.FullStftStructuredPreview(),
        };
        var catalog = OnDemandDetailClient.Catalog(client, "stft", "dB re 1 uV^2/Hz");
        var detail = new AlgorithmDetailViewModel(catalog).Stft;
        catalog.SelectedDynamicExecutionMode = "按需预览 + 后台全量";

        await OnDemandDetailClient.Run(catalog);
        await catalog.CurrentWindowLoad;
        var requestCountAfterFullCache = client.Requests.Count;
        catalog.DynamicTimeProgressPercent = 50;
        await catalog.CurrentWindowLoad;

        Assert.Equal(1, client.StructuredPreviewCalls);
        Assert.Equal(0, client.FullWindowPreviewCalls);
        Assert.Equal(requestCountAfterFullCache, client.Requests.Count);
        Assert.Equal("全量已完成窗口（本地缓存）", catalog.DynamicPreviewSourceText);
        Assert.NotNull(detail.Preview);
        Assert.Equal([12d, 13d], detail.Preview!.TimesSeconds);
        Assert.Equal(2, detail.Preview.PowerDb.Length);
        Assert.StartsWith("full-cache:", catalog.DisplayedAnalysisWindow!.SessionId);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Combined_rbp_serves_completed_full_metric_window_without_http_preview()
    {
        var client = new OnDemandDetailClient
        {
            Respond = OnDemandDetailClient.RbpResponse,
            RespondFullRun = request => new AnalysisRunResponse(
                "full-run-1", request.RecordingId, request.AnalysisType, "completed", "fixture-v1", null, null,
                JsonSerializer.SerializeToElement(new
                {
                    metric = new
                    {
                        output = new { unit = "ratio" },
                        series = new[]
                        {
                            new { time_s = 5d, window_start_s = 0d, window_end_s = 5d, value = (double?)null,
                                analysis_state = "Complete", quality = new { status = "clean" },
                                band_values = new { delta = 0.1, theta = 0.2, alpha = 0.3, beta = 0.25, gamma = 0.15 } },
                            new { time_s = 10d, window_start_s = 5d, window_end_s = 10d, value = (double?)null,
                                analysis_state = "Complete", quality = new { status = "clean" },
                                band_values = new { delta = 0.2, theta = 0.2, alpha = 0.2, beta = 0.2, gamma = 0.2 } },
                        },
                    },
                }),
                null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 10, 10,
                "completed", "已完成"),
        };
        var catalog = OnDemandDetailClient.Catalog(client, "rbp");
        catalog.SelectedDynamicExecutionMode = "按需预览 + 后台全量";

        await OnDemandDetailClient.Run(catalog);
        var requestCountAfterFullCache = client.Requests.Count;
        catalog.DynamicTimeProgressPercent = 50;
        await catalog.CurrentWindowLoad;

        Assert.Equal(0, client.StructuredPreviewCalls);
        Assert.Equal(0, client.FullWindowPreviewCalls);
        Assert.Equal(requestCountAfterFullCache, client.Requests.Count);
        Assert.Equal("全量已完成窗口（本地缓存）", catalog.DynamicPreviewSourceText);
        Assert.StartsWith("full-metric:", catalog.DisplayedAnalysisWindow!.SessionId);
        Assert.Equal("ready", catalog.DisplayedAnalysisWindow.State);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Combined_scalar_detail_releases_completed_full_series_through_cursor()
    {
        var client = new OnDemandDetailClient
        {
            Respond = ScalarWindowTestSupport.Response,
            RespondFullRun = request => new AnalysisRunResponse(
                "full-run-iapf", request.RecordingId, request.AnalysisType, "completed", "fixture-v1", null, null,
                JsonSerializer.SerializeToElement(new
                {
                    metric = new
                    {
                        channel = "F3",
                        official = new { algorithm_id = "iapf" },
                        output = new { unit = "Hz" },
                        series = new[]
                        {
                            new { time_s = 5d, window_start_s = 0d, window_end_s = 5d,
                                value = 9.5, analysis_state = "Complete", quality = new { status = "clean" } },
                            new { time_s = 10d, window_start_s = 5d, window_end_s = 10d,
                                value = 10.25, analysis_state = "Complete", quality = new { status = "clean" } },
                        },
                    },
                }),
                null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 10, 10,
                "completed", "已完成"),
        };
        var catalog = OnDemandDetailClient.Catalog(client, "iapf", "Hz");
        var detail = new AlgorithmDetailViewModel(catalog).Iapf;
        catalog.SelectedDynamicExecutionMode = "按需预览 + 后台全量";

        await OnDemandDetailClient.Run(catalog);
        // A completed Full Run is the local source, but the chart must not
        // paint a future window before the cursor reaches it.
        catalog.DynamicTimeProgressPercent = 20;
        await catalog.CurrentWindowLoad;

        Assert.Single(detail.TrendPoints);
        Assert.Equal([9.5], detail.TrendPoints.Select(point => point.ValueHz).ToArray());

        // Moving to 12 seconds reveals the second completed one-second-step
        // window from the full result without submitting another calculation.
        catalog.DynamicTimeProgressPercent = 40;
        await catalog.CurrentWindowLoad;
        Assert.Equal(2, detail.TrendPoints.Count);
        Assert.Equal([9.5, 10.25], detail.TrendPoints.Select(point => point.ValueHz).ToArray());
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Repeating_same_combined_configuration_does_not_submit_duplicate_full_run()
    {
        var client = new OnDemandDetailClient
        {
            RespondFullRun = request => new AnalysisRunResponse(
                "full-run-1", request.RecordingId, request.AnalysisType, "completed", "fixture-v1", null, null,
                null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 10, 10,
                "completed", "已完成"),
        };
        var catalog = OnDemandDetailClient.Catalog(client);
        catalog.SelectedDynamicExecutionMode = "按需预览 + 后台全量";

        await OnDemandDetailClient.Run(catalog);
        await OnDemandDetailClient.Run(catalog);

        Assert.Equal(1, client.FullRunCalls);
        Assert.Single(client.FullRunRequests);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Changing_scientific_configuration_hides_previous_background_progress()
    {
        var client = new OnDemandDetailClient
        {
            RespondFullRun = request => new AnalysisRunResponse(
                "full-run-1", request.RecordingId, request.AnalysisType, "completed", "fixture-v1", null, null,
                null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 10, 10, "completed", "已完成"),
        };
        var catalog = OnDemandDetailClient.Catalog(client);
        catalog.SelectedDynamicExecutionMode = "按需预览 + 后台全量";

        await OnDemandDetailClient.Run(catalog);

        Assert.NotNull(catalog.BackgroundFullRun);
        catalog.LowFrequencyText = "2";

        Assert.Null(catalog.BackgroundFullRun);
        Assert.Equal("未启动", catalog.BackgroundFullRunProgressText);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Cancelling_background_full_run_keeps_preview_window_available()
    {
        var client = new OnDemandDetailClient();
        var catalog = OnDemandDetailClient.Catalog(client);
        await OnDemandDetailClient.Run(catalog);
        var displayed = catalog.DisplayedAnalysisWindow;
        var background = new AnalysisRunResponse(
            "background-run", "recording", "official_algorithm", "running", "fixture-v1", null, null,
            null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 2, 10,
            "analysis", "2/10");
        typeof(AlgorithmListViewModel).GetProperty(nameof(AlgorithmListViewModel.BackgroundFullRun))!
            .SetValue(catalog, background);

        await (Task)typeof(AlgorithmListViewModel)
            .GetMethod("CancelBackgroundFullRunAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(catalog, null)!;

        Assert.Equal(1, client.CancelledRuns);
        Assert.Equal("cancelled", catalog.BackgroundFullRun!.Status);
        Assert.Same(displayed, catalog.DisplayedAnalysisWindow);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Background_observer_error_does_not_overwrite_preview_status()
    {
        var client = new OnDemandDetailClient
        {
            RespondFullRun = _ => throw new InvalidOperationException("后台连接失败"),
        };
        var catalog = OnDemandDetailClient.Catalog(client);
        catalog.SelectedDynamicExecutionMode = "按需预览 + 后台全量";

        await OnDemandDetailClient.Run(catalog);

        Assert.StartsWith("按需窗口：", catalog.RunStatusText);
        Assert.Contains("后台全量观察失败", catalog.BackgroundFullRunStatusText);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Suspending_detail_stops_polling_and_resume_observes_same_background_run()
    {
        var client = new OnDemandDetailClient
        {
            RespondFullRun = request => new AnalysisRunResponse(
                "full-run-1", request.RecordingId, request.AnalysisType, "running", "fixture-v1", null, null,
                null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 2, 10,
                "analysis", "2/10"),
        };
        client.PollResponses.Enqueue(new AnalysisRunResponse(
            "full-run-1", "recording", "official_algorithm", "completed", "fixture-v1", null, null,
            null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 10, 10,
            "completed", "已完成"));
        var catalog = OnDemandDetailClient.Catalog(client);
        catalog.SelectedDynamicExecutionMode = "按需预览 + 后台全量";

        await OnDemandDetailClient.Run(catalog);
        await catalog.SuspendDetailAsync();

        Assert.Equal(0, client.GetRunCalls);
        catalog.ResumeDetail();
        await Task.Delay(650);

        Assert.Equal(1, client.GetRunCalls);
        Assert.Equal("completed", catalog.BackgroundFullRun!.Status);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public void Review_shares_psd_availability_and_preserves_injected_override()
    {
        var client = new OnDemandDetailClient();
        Assert.True(new PsdReviewAnalysisProvider(client).SupportsOnDemandWindows);
        Environment.SetEnvironmentVariable(Flag("psd"), "0");
        Assert.False(new PsdReviewAnalysisProvider(client).SupportsOnDemandWindows);
        Assert.True(new PsdReviewAnalysisProvider(client, enableOnDemand: true).SupportsOnDemandWindows);
        Environment.SetEnvironmentVariable(Flag("psd"), "1");
        Assert.False(new PsdReviewAnalysisProvider(client, enableOnDemand: false).SupportsOnDemandWindows);
    }

    [Fact]
    public void Unapproved_modules_remain_off_and_empty_override_uses_module_default()
    {
        Assert.False(OfflineAnalysisAvailability.IsOnDemandEnabled("future"));
        Environment.SetEnvironmentVariable(Flag("future"), "1");
        Assert.True(OfflineAnalysisAvailability.IsOnDemandEnabled("future"));
        Environment.SetEnvironmentVariable(Flag("psd"), "");
        Assert.True(new PsdReviewAnalysisProvider(new OnDemandDetailClient()).SupportsOnDemandWindows);
    }

    private static string Flag(string id) => $"BRAIN_WPF_ON_DEMAND_{id.ToUpperInvariant()}";
}
