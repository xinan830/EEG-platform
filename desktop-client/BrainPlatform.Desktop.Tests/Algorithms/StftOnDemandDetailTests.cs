using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;

namespace BrainPlatform.Desktop.Tests.Algorithms;

[Collection("OnDemandWindowFeature")]
public sealed class StftOnDemandDetailTests : IDisposable
{
    private readonly string? previous = Environment.GetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_STFT");

    public StftOnDemandDetailTests() => Environment.SetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_STFT", "1");

    public void Dispose() => Environment.SetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_STFT", previous);

    [Fact]
    public async Task Ready_window_projects_relative_time_frequency_matrix_without_full_run()
    {
        var client = new OnDemandDetailClient { Respond = Response };
        var catalog = OnDemandDetailClient.Catalog(client, "stft", unit: "dB re 1 uV^2/Hz");
        var detail = new AlgorithmDetailViewModel(catalog).Stft;

        await OnDemandDetailClient.Run(catalog);
        Assert.Equal("insufficient_data", catalog.DisplayedAnalysisWindow!.State);
        catalog.DynamicTimeProgressPercent = 20;
        await catalog.CurrentWindowLoad;

        Assert.NotNull(detail.Preview);
        Assert.Equal([3d, 4d], detail.Preview!.TimesSeconds);
        Assert.Equal(2, detail.Preview.PowerDb.Length);
        Assert.Equal(2, detail.Preview.PowerDb[0].Length);
        Assert.Null(catalog.LastRun);
        Assert.Contains("当前窗口", detail.StatusText);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Quality_rejection_keeps_window_reason_and_no_chart()
    {
        var client = new OnDemandDetailClient
        {
            Respond = request => Response(request) with
            {
                State = "rejected",
                Quality = "gate_failed",
                Result = null,
                Failure = new DynamicWindowFailure("STFT_WINDOW_QUALITY_GATE_FAILED", "质量门拒绝",
                    JsonSerializer.SerializeToElement(new { peak_uv = 350, threshold_uv = 200 })),
            },
        };
        var catalog = OnDemandDetailClient.Catalog(client, "stft");
        var detail = new AlgorithmDetailViewModel(catalog).Stft;

        await OnDemandDetailClient.Run(catalog);
        catalog.DynamicTimeProgressPercent = 20;
        await catalog.CurrentWindowLoad;

        Assert.Null(detail.Preview);
        Assert.Equal(1, detail.RejectedWindowCount);
        Assert.Contains("350", detail.FailureDetailsText);
        Assert.Contains("STFT_WINDOW_QUALITY_GATE_FAILED", detail.FailureReasonText);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public void Invalid_axis_unit_or_provenance_is_rejected()
    {
        var response = Response(new DynamicWindowRequest("recording", "stft", null, null,
            JsonSerializer.SerializeToElement(new { }), 1200, RequestId: "request"));
        var state = new StftPreviewState();
        Assert.Throws<InvalidOperationException>(() => StftWindowPresenter.Apply(state, response with
        {
            Result = response.Result! with
            {
                AxisUnits = new Dictionary<string, string> { ["time_center_s"] = "ms", ["frequency_hz"] = "Hz" },
            },
        }));
        Assert.Null(state.WindowPreview);
        Assert.Throws<InvalidOperationException>(() => StftWindowPresenter.Apply(state, response with
        {
            Result = response.Result! with
            {
                Provenance = JsonSerializer.SerializeToElement(new { preprocessing_scope = "local_window" }),
            },
        }));
        Assert.Null(state.WindowPreview);
    }

    [Fact]
    public void Overlapping_windows_append_only_new_absolute_time_columns()
    {
        var first = Response(new DynamicWindowRequest("recording", "stft", null, null,
            JsonSerializer.SerializeToElement(new { }), 1000, RequestId: "first"));
        var second = Response(new DynamicWindowRequest("recording", "stft", null, null,
            JsonSerializer.SerializeToElement(new { }), 1200, RequestId: "second"));
        var state = new StftPreviewState();

        StftWindowPresenter.Append(state, first);
        StftWindowPresenter.Append(state, second);

        Assert.Equal([2d, 3d, 4d], state.Result!.TimesSeconds);
        Assert.Equal([1d, 1d, 1d], state.Result.PowerDb.Select(row => row[0]));
    }

    [Fact]
    public void Incompatible_session_clears_the_stft_history()
    {
        var response = Response(new DynamicWindowRequest("recording", "stft", null, null,
            JsonSerializer.SerializeToElement(new { }), 1000, RequestId: "first"));
        var state = new StftPreviewState();
        StftWindowPresenter.Append(state, response);
        StftWindowPresenter.Append(state, response with { SessionId = "new-session" });

        Assert.Equal([2d, 3d], state.Result!.TimesSeconds);
    }

    [Theory]
    [InlineData(800, 0, 4, 2)]
    [InlineData(3000, 10, 15, 12)]
    [InlineData(5000, 20, 25, 22)]
    public void Relative_window_times_are_projected_to_recording_time_once(
        long cursorSample, double start, double end, double firstCenter)
    {
        var response = Response(new DynamicWindowRequest("recording", "stft", null, null,
            JsonSerializer.SerializeToElement(new { }), cursorSample, RequestId: "request"));
        var preview = StftPreview.FromWindow(response);
        Assert.Equal(firstCenter, preview.TimesSeconds[0]);
        Assert.Equal(new TimeRange(start, end), preview.RequestedRange);
        Assert.Equal(cursorSample < 1000 ? 1 : 2, preview.TimesSeconds.Length);
        Assert.Equal(1, preview.PowerDb[0][0]);
    }

    [Theory]
    [InlineData("absolute_recording_time_center_s", 12, 13)]
    [InlineData("relative_window_time_center_s", 12, 13)]
    [InlineData("relative_window_time_center_s", -2, 3)]
    public void Legacy_or_out_of_window_coordinates_are_rejected(string axisKind, double first, double second)
    {
        var response = Response(new DynamicWindowRequest("recording", "stft", null, null,
            JsonSerializer.SerializeToElement(new { }), 3000, RequestId: "request"));
        var result = response.Result!;
        var axes = result.Axes!.ToDictionary(pair => pair.Key, pair => pair.Value);
        axes["time_center_s"] = JsonSerializer.SerializeToElement(new[] { first, second });
        var invalid = response with { Result = result with
        {
            Axes = axes,
            Provenance = JsonSerializer.SerializeToElement(new
            {
                preprocessing_scope = "whole_recording_before_window_slice",
                inner_time_axis = axisKind,
                analysis_state = "Complete",
            }),
        } };
        Assert.Throws<InvalidOperationException>(() => StftPreview.FromWindow(invalid));
    }

    private static DynamicWindowResponse Response(DynamicWindowRequest request)
    {
        var end = request.CursorSample / 200d;
        var start = Math.Max(0, end - 5);
        if (end < 4)
            return new DynamicWindowResponse(
                "session", $"window-{request.CursorSample}", request.RequestId ?? "request", "insufficient_data",
                JsonSerializer.SerializeToElement(new { recording_id = request.RecordingId }), null,
                new DynamicWindowRange(0, request.CursorSample, 0, end), "insufficient_data",
                new DynamicWindowFailure("STFT_WINDOW_INSUFFICIENT_DATA", "数据不足"), null, "subscription");
        var partial = end < 5;
        var times = partial ? new[] { 2d } : new[] { 2d, 3d };
        var result = new DynamicWindowResultResponse(
            null, "ready", "time_frequency",
            new Dictionary<string, JsonElement>
            {
                ["time_center_s"] = JsonSerializer.SerializeToElement(times),
                ["frequency_hz"] = JsonSerializer.SerializeToElement(new[] { 10d, 11d }),
            },
            new Dictionary<string, string> { ["time_center_s"] = "s", ["frequency_hz"] = "Hz" },
            new Dictionary<string, JsonElement>
            {
                ["power_linear"] = JsonSerializer.SerializeToElement(times.Select(_ => new[] { 1e-12, 2e-12 }).ToArray()),
                ["power_db"] = JsonSerializer.SerializeToElement(times.Select(_ => new[] { 1d, 2d }).ToArray()),
            },
            new Dictionary<string, string> { ["power_linear"] = "V^2/Hz", ["power_db"] = "dB re 1 uV^2/Hz" },
            "clean", null,
            JsonSerializer.SerializeToElement(new { source_quality = new { clean_windows = 1, total_windows = 1 }, calculation_trace = new { segment_s = 4 } }),
            JsonSerializer.SerializeToElement(new
            {
                preprocessing_scope = "whole_recording_before_window_slice",
                inner_time_axis = "relative_window_time_center_s",
                analysis_state = partial ? "Partial" : "Complete",
            }));
        return new DynamicWindowResponse(
            "session", $"window-{request.CursorSample}", request.RequestId ?? "request", "ready",
            JsonSerializer.SerializeToElement(new { recording_id = request.RecordingId }), null,
            new DynamicWindowRange((long)Math.Max(0, (end - 5) * 200), request.CursorSample, start, end),
            "clean", null, result, "subscription");
    }
}
