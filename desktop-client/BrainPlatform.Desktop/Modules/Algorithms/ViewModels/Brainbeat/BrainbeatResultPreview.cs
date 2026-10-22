using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Brainbeat;

public sealed record BrainbeatPoint(
    double TimeSeconds,
    double? Value,
    string State,
    string Quality,
    string Failure,
    double StartSeconds = double.NaN,
    double EndSeconds = double.NaN);

public sealed class BrainbeatResultPreview
{
    private BrainbeatResultPreview(double? value, string unit, IReadOnlyList<BrainbeatPoint> points, string channel)
    {
        Value = value;
        Unit = unit;
        Points = points;
        Channel = channel;
    }

    public double? Value { get; }
    public string Unit { get; }
    public IReadOnlyList<BrainbeatPoint> Points { get; }
    public string Channel { get; }
    public bool IsDynamic => Points.Count > 0;

    public static BrainbeatResultPreview FromWindow(DynamicWindowResponse response, string channel,
        BrainbeatResultPreview? previous = null)
    {
        var projection = ScalarWindowProjectionParser.Parse(response, "brainbeat", "ratio", "brainbeat_evidence");
        var sourceLabel = string.IsNullOrWhiteSpace(channel) ? "Fz / Pz" : channel;
        if (response.State == "ready")
        {
            if (response.Result?.Provenance is not { ValueKind: JsonValueKind.Object } provenance ||
                Text(provenance, "preprocessing_scope") != "whole_recording_before_window_slice" ||
                !Bool(provenance, "paired_sources") ||
                !Bool(provenance, "independent_windows") ||
                !provenance.TryGetProperty("cross_window_ema", out var ema) || ema.ValueKind != JsonValueKind.False ||
                !Bool(provenance, "iapf_dependency") ||
                !provenance.TryGetProperty("source_channels", out var sources) || sources.ValueKind != JsonValueKind.Object ||
                !MatchesSources(sources, projection.Evidence, sourceLabel) ||
                ScalarWindowProjectionParser.Number(projection.Evidence, "iapf_hz") is not > 0)
                throw new InvalidOperationException("Brainbeat 按需结果缺少双通道独立窗口 provenance。");
        }
        var point = new BrainbeatPoint(
            projection.TimeSeconds, projection.Value, projection.State, projection.Quality,
            projection.Failure, projection.StartSeconds, projection.EndSeconds);
        var points = ScalarWindowProjectionParser.Merge(
            previous?.Points ?? [], point,
            item => item.StartSeconds, item => item.EndSeconds);
        return new BrainbeatResultPreview(null, "ratio", points,
            sourceLabel);
    }

    private static bool MatchesSources(JsonElement sources, JsonElement evidence, string channel)
    {
        var fz = Text(sources, "fz");
        var pz = Text(sources, "pz");
        return fz.Length > 0 && pz.Length > 0 &&
            string.Equals($"{fz} / {pz}", channel, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Text(evidence, "fz_channel"), fz, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Text(evidence, "pz_channel"), pz, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Bool(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) &&
        item.ValueKind == JsonValueKind.True;

    public static BrainbeatResultPreview? Parse(AnalysisRunResponse? run)
    {
        if (run is not null) CurrentAlgorithmResult.Require(run);
        if (run?.ResultSummary is not { ValueKind: JsonValueKind.Object } summary ||
            !summary.TryGetProperty("metric", out var metric) || metric.ValueKind != JsonValueKind.Object)
            return null;
        var channel = Text(metric, "channel");
        var unit = metric.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Object
            ? Text(output, "unit") : "";
        if (metric.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
        {
            var points = series.EnumerateArray().Select(point =>
            {
                var time = Number(point, "time_s");
                var start = Number(point, "window_start_s");
                var end = Number(point, "window_end_s");
                return new BrainbeatPoint(time, NumberOrNull(point.GetProperty("output"), "value"), Text(point, "analysis_state"),
                    Quality(point), Failure(point), start, end);
            }).ToArray();
            return new BrainbeatResultPreview(null, unit, points, channel);
        }
        var value = NumberOrNull(metric.GetProperty("output"), "value");
        return new BrainbeatResultPreview(value, unit, [], channel);
    }

    private static string Quality(JsonElement point) => point.TryGetProperty("quality", out var quality)
        ? Text(quality, "status") : "未提供";
    private static string Failure(JsonElement point) => point.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object
        ? Text(failure, "message") : "";
    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : "";
    private static double Number(JsonElement value, string name) => value.TryGetProperty(name, out var element) && element.TryGetDouble(out var number) ? number : double.NaN;
    private static double? NumberOrNull(JsonElement value, string name) => value.TryGetProperty(name, out var element) ? NumberOrNull(element) : null;
    private static double? NumberOrNull(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
}
