using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Rbp;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Stft;
using BrainPlatform.Desktop.Shared.Time;
using BrainPlatform.Desktop.Shared.Waveform;
using SciChart.Charting.Visuals;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class RecordingTimeChartModeTests
{
    [Fact]
    public void Rbp_time_mode_does_not_leak_into_band_categories() => OnSta(() =>
    {
        var start = DateTimeOffset.UtcNow;
        var context = new RecordingTimeDisplayContext(new RecordingTimeDisplaySettings(), start);
        var run = new AnalysisRunResponse("run", "recording", "official_algorithm", "completed", "official-rbp-v2", null, null,
            Json("""{"metric":{"series":[{"time_s":5,"window_start_s":0,"window_end_s":5,"analysis_state":"Complete","band_values":{"delta":0.1,"theta":0.2,"alpha":0.3,"beta":0.2,"gamma":0.2},"result_contract_version":"dynamic-analysis-frame-v6","output":{"value":null,"unit":"ratio"},"quality":{"status":"clean"}}],"output":{"unit":"ratio","value":null},"actual_range":{"start_s":0,"end_s":30},"mode":"dynamic","result_contract_version":"dynamic-analysis-frame-v6"},"result_contract_version":"official-algorithm-result-v1"}"""),
            null, null, start, start);
        var preview = RbpResultPreview.Parse(run);
        var chart = new RbpChart { IsTrend = true, Bands = preview.BandsAt(5), Windows = preview.Windows };
        RecordingTimeAxisPresentation.SetContext(chart, context);
        var surface = Assert.IsType<SciChartSurface>(chart.Content);
        var axis = surface.XAxes[0];
        var data = surface.RenderableSeries[0].DataSeries;
        context.IsClockTime = true;
        Assert.Equal(context.FormatAxis(5), axis.LabelProvider.FormatLabel(5d));
        Assert.Same(data, surface.RenderableSeries[0].DataSeries);

        chart.IsTrend = false;
        context.IsRecordTime = true;
        context.IsClockTime = true;
        Assert.Equal("频段", axis.AxisTitle);
        Assert.Equal(chart.Bands![0].Label, axis.LabelProvider.FormatLabel(0d));
        chart.IsTrend = true;
        Assert.Equal(context.FormatAxis(5), axis.LabelProvider.FormatLabel(5d));
    });

    [Fact]
    public void Stft_time_axis_switch_preserves_matrix_and_restores_frequency_axis() => OnSta(() =>
    {
        var start = DateTimeOffset.UtcNow;
        var context = new RecordingTimeDisplayContext(new RecordingTimeDisplaySettings(), start);
        var preview = StftPreview.Parse(new StructuredPreviewResponse("run",
            new RunArtifact("a", "run", "stft", "stft.npz", "application/x-npz", 1, "hash", null, new Dictionary<string, int[]>(), start),
            Json("""{"kind":"time_frequency"}"""), ["F3"], new TimeRange(0, 4), new TimeRange(0, 4),
            new Dictionary<string, JsonElement> { ["time_center_s"] = Json("[1,2]"), ["frequency_hz"] = Json("[4,8]") },
            new Dictionary<string, JsonElement> { ["time_center_s"] = Json("""{"unit":"s"}"""), ["frequency_hz"] = Json("""{"unit":"Hz"}""") },
            new Dictionary<string, JsonElement> { ["power_db"] = Json("[[1,2],[3,4]]") },
            new Dictionary<string, JsonElement> { ["power_db"] = Json("""{"unit":"dB re 1 uV^2/Hz"}""") },
            [], new Dictionary<string, int>(), null, "spectrogram-v2", "stft-runtime-v1"));
        var chart = new StftSliceChart { Preview = preview, IsSpectrum = false };
        RecordingTimeAxisPresentation.SetContext(chart, context);
        var surface = Assert.IsType<SciChartSurface>(chart.Content);
        var axis = surface.XAxes[0];
        var matrix = preview.PowerDb;
        var range = axis.VisibleRange;
        context.IsClockTime = true;
        Assert.Equal(context.FormatAxis(2), axis.LabelProvider.FormatLabel(2d));
        Assert.Contains("中心", axis.AxisTitle);
        Assert.Same(matrix, preview.PowerDb);
        Assert.Same(range, axis.VisibleRange);

        chart.IsSpectrum = true;
        context.IsRecordTime = true;
        context.IsClockTime = true;
        Assert.Equal("频率（Hz）", axis.AxisTitle);
        Assert.DoesNotContain(":", axis.LabelProvider.FormatLabel(4d));
        chart.IsSpectrum = false;
        Assert.Equal(context.FormatAxis(2), axis.LabelProvider.FormatLabel(2d));
    });

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
