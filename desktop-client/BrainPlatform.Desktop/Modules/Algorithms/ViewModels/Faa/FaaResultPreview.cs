using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Faa;

public sealed record FaaEvidence(string LeftChannel, string RightChannel, double? LeftPowerUv2, double? RightPowerUv2, double? CleanEpochs, double? TotalEpochs, double? CleanRatio, string AlphaBand, string Formula);

public sealed record FaaResultPoint(double TimeSeconds, double StartSeconds, double EndSeconds, double? Value, string State, string Quality, string Failure, FaaEvidence Evidence)
{
    public bool IsComplete => State == "Complete" && Value is not null;
}

public sealed record FaaResultPreview(string Channel, FaaResultPoint? StaticPoint, IReadOnlyList<FaaResultPoint> DynamicPoints)
{
    public bool IsDynamic => DynamicPoints.Count > 0;
    public FaaResultPoint? CurrentAt(double cursor) => IsDynamic ? DynamicPoints.LastOrDefault(point => point.EndSeconds <= cursor + 1e-9) : StaticPoint;
    public IReadOnlyList<FaaResultPoint> VisibleThrough(double cursor) => IsDynamic ? DynamicPoints.Where(point => point.EndSeconds <= cursor + 1e-9).ToArray() : [];

    public static FaaResultPreview FromWindow(DynamicWindowResponse response, FaaResultPreview? previous = null)
    {
        var range = response.Range;
        if (range is null)
            return previous ?? new FaaResultPreview("", null, []);
        var projected = ScalarWindowProjectionParser.Parse(response, "faa", "dimensionless", "faa_evidence");
        if (response.State == "ready")
        {
            if (response.Result?.Provenance is not { ValueKind: JsonValueKind.Object } provenance ||
                Text(provenance, "preprocessing_scope") != "whole_recording_before_window_slice" ||
                !provenance.TryGetProperty("paired_sources", out var paired) || paired.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("FAA 按需结果缺少成对来源或连续预处理 provenance。");
        }
        var rawEvidence = response.Result?.Evidence;
        if (rawEvidence is null && response.Failure?.Details is { ValueKind: JsonValueKind.Object } details &&
            details.TryGetProperty("evidence", out var failureEvidence)) rawEvidence = failureEvidence;
        var evidence = rawEvidence is { ValueKind: JsonValueKind.Object } payload ? Evidence(payload) : EmptyEvidence;
        var point = new FaaResultPoint(range.EndSeconds, range.StartSeconds, range.EndSeconds,
            projected.Value, projected.State, projected.Quality, projected.Failure, evidence);
        var points = ScalarWindowProjectionParser.Merge(previous?.DynamicPoints ?? [], point,
            item => item.StartSeconds, item => item.EndSeconds);
        var channel = previous?.Channel;
        if (string.IsNullOrWhiteSpace(channel) && evidence.LeftChannel.Length > 0 && evidence.RightChannel.Length > 0)
            channel = $"{evidence.LeftChannel}/{evidence.RightChannel}";
        return new FaaResultPreview(channel ?? "", null, points);
    }

    public static FaaResultPreview? Parse(JsonElement? summary)
    {
        if (summary is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty("metric", out var metric) || metric.ValueKind != JsonValueKind.Object || !metric.TryGetProperty("official", out var official) || Text(official, "algorithm_id") != "faa" || !metric.TryGetProperty("output", out var output) || Text(output, "unit") != "dimensionless") return null;
        var channel = Text(metric, "channel");
        if (metric.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
            return new FaaResultPreview(channel, null, series.EnumerateArray().Select(ParsePoint).Where(point => point is not null).Cast<FaaResultPoint>().OrderBy(point => point.EndSeconds).ToArray());
        var range = metric.TryGetProperty("actual_range", out var actual) ? actual : default;
        var value = Number(output, "value") ?? Number(metric, "value");
        return new FaaResultPreview(channel, new FaaResultPoint(double.NaN, Number(range, "start_s") ?? double.NaN, Number(range, "end_s") ?? double.NaN, value, value is null ? "Unavailable" : "Complete", Quality(metric), Failure(metric), Evidence(official)), []);
    }

    private static FaaResultPoint? ParsePoint(JsonElement point)
    {
        var time = Number(point, "time_s"); var start = Number(point, "window_start_s"); var end = Number(point, "window_end_s");
        if (time is null || start is null || end is null || end <= start) return null;
        var state = Text(point, "analysis_state");
        return new FaaResultPoint(time.Value, start.Value, end.Value, state == "Complete" ? Number(point, "value") : null, state, Quality(point), Failure(point), Evidence(point));
    }

    private static FaaEvidence Evidence(JsonElement point)
    {
        var official = point.ValueKind == JsonValueKind.Object && point.TryGetProperty("faa_evidence", out _) ? point : point.ValueKind == JsonValueKind.Object && point.TryGetProperty("official", out var value) && value.ValueKind == JsonValueKind.Object ? value : point;
        var evidence = official.ValueKind == JsonValueKind.Object && official.TryGetProperty("faa_evidence", out var direct) && direct.ValueKind == JsonValueKind.Object ? direct :
            official.ValueKind == JsonValueKind.Object && official.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object && extensions.TryGetProperty("faa_evidence", out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : default;
        var channels = evidence.ValueKind == JsonValueKind.Object && evidence.TryGetProperty("source_channels", out var source) && source.ValueKind == JsonValueKind.Object ? source : default;
        var band = evidence.ValueKind == JsonValueKind.Object && evidence.TryGetProperty("band", out var bandValue) && bandValue.ValueKind == JsonValueKind.Array && bandValue.GetArrayLength() >= 2 ? $"{Number(bandValue[0]):0.##}–{Number(bandValue[1]):0.##} Hz" : "8–13 Hz";
        return new FaaEvidence(Text(channels, "left"), Text(channels, "right"), Number(evidence, "p_f3") * 1e12, Number(evidence, "p_f4") * 1e12, Number(evidence, "clean_epochs"), Number(evidence, "total_epochs"), Number(evidence, "clean_ratio"), band, Text(evidence, "formula"));
    }
    private static readonly FaaEvidence EmptyEvidence = new("", "", null, null, null, null, null, "8–13 Hz", "");
    private static string Quality(JsonElement value) => value.TryGetProperty("quality", out var quality) ? Text(quality, "status") : "";
    private static string Failure(JsonElement value) { if (value.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object) return Text(failure, "message") is { Length: > 0 } message ? message : Text(failure, "code"); if (value.TryGetProperty("quality", out var quality) && quality.ValueKind == JsonValueKind.Object && quality.TryGetProperty("reasons", out var reasons) && reasons.ValueKind == JsonValueKind.Array) return string.Join("、", reasons.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString())); return ""; }
    private static string Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
    private static double? Number(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) ? Number(item) : null;
    private static double? Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
}
