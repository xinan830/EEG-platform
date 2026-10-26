using System.Globalization;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

/// <summary>
/// A backend-returned trend point. <see cref="IsScheduledGap"/> distinguishes
/// an intentional cadence hold from a computed unavailable result.
/// </summary>
public sealed record TnkChartPoint(double X, double? Value, string Label, bool IsScheduledGap = false);
public sealed record TnkChartSeries(string Name, string Unit, IReadOnlyList<TnkChartPoint> Points);
public sealed record TnkResultChart(bool IsTimeline, IReadOnlyList<TnkChartSeries> Series)
{
    public bool HasValues => Series.Any(series => series.Points.Any(point => point.Value.HasValue));
    public string Caption => !HasValues ? "暂无有效数值；请查看本模块质量与原因。"
        : IsTimeline ? "趋势图 · 横轴为窗口结束的记录秒数；缺失值留空"
        : "当前窗口数值图 · 横轴为来源通道 / 指标；悬停查看原始数值";
}

/// <summary>Projects returned evidence only; no scientific calculations or imputed values.</summary>
internal static class TnkResultChartProjection
{
    internal static IReadOnlyList<TnkChildResultDisplay> ReadRun(AnalysisRunResponse? run, JsonElement schema)
    {
        var cards = TnkGroupResultProjection.ReadResults(run, schema);
        if (run?.ResultSummary is not { } summary || cards.Count == 0) return cards;
        var evidence = summary.GetProperty("structured").GetProperty("official").GetProperty("tnk_evidence");
        if (!evidence.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
            return cards.Select(card => card with { Chart = Snapshot(evidence, card.AlgorithmId, run.ActualRange?.EndSeconds) }).ToArray();
        return cards.Select(card => card with { Chart = Timeline(points, card.AlgorithmId) }).ToArray();
    }

    internal static IReadOnlyList<TnkChildResultDisplay> ReadWindow(DynamicWindowResponse? window, JsonElement schema)
    {
        if (window?.Identity is not { ValueKind: JsonValueKind.Object } identity ||
            Text(identity, "algorithm_id") != "tnk_biofeedback") return [];
        var definitions = TnkGroupResultProjection.ReadDefinitions(schema);
        if (window.Result is not { OutputKind: "algorithm_group", Evidence: { ValueKind: JsonValueKind.Object } evidence } ||
            !evidence.TryGetProperty("extensions", out var extensions) ||
            !extensions.TryGetProperty("tnk_evidence", out var tnk) ||
            !tnk.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Object)
            return definitions.Select(definition => new TnkChildResultDisplay(definition.AlgorithmId,
                definition.DisplayName, definition.RequiredRolesText, "尚无可显示来源", "不可用",
                AlgorithmResultFormatter.FormatQualityStatus(window.Quality ?? window.State),
                window.Failure is { } failure ? $"{failure.Code}：{failure.Message}" : "当前目标尚无可用结果。")).ToArray();
        return definitions.Select(definition =>
            TnkGroupResultProjection.ProjectChild(definition, children) with
            { Chart = Snapshot(tnk, definition.AlgorithmId, window.Range?.EndSeconds) }).ToArray();
    }

    private static TnkResultChart Snapshot(JsonElement evidence, string id, double? endSeconds)
    {
        var series = Values(evidence, id).Select(item =>
        {
            var time = double.IsFinite(item.Time) ? item.Time : endSeconds;
            return new TnkChartSeries(item.Key, item.Unit, time is double seconds && double.IsFinite(seconds)
                ? [new(seconds, item.Value, $"{seconds:G8} s")] : []);
        }).ToArray();
        return new(true, series);
    }

    private static TnkResultChart Timeline(JsonElement points, string id)
    {
        var values = new Dictionary<string, List<TnkChartPoint>>();
        var units = new Dictionary<string, string>();
        foreach (var point in points.EnumerateArray())
        {
            if (!point.TryGetProperty("extensions", out var extensions) ||
                !extensions.TryGetProperty("tnk_evidence", out var evidence)) continue;
            foreach (var item in Values(evidence, id))
            {
                if (!double.IsFinite(item.Time)) continue;
                if (!values.TryGetValue(item.Key, out var list)) values[item.Key] = list = [];
                units[item.Key] = item.Unit;
                list.Add(new(item.Time, item.Value, $"{item.Time.ToString("G8", CultureInfo.InvariantCulture)} s", item.IsScheduledGap));
            }
        }
        return new(true, values.Select(item => new TnkChartSeries(item.Key, units[item.Key], item.Value)).ToArray());
    }

    private static IEnumerable<(string Key, string Unit, double? Value, double Time, bool IsScheduledGap)> Values(JsonElement evidence, string id)
    {
        if (!evidence.TryGetProperty("children", out var children) ||
            !children.TryGetProperty(id, out var rows) || rows.ValueKind != JsonValueKind.Array) yield break;
        foreach (var row in rows.EnumerateArray())
        {
            var channel = Text(row, "channel");
            var unit = Text(row, "unit");
            var isScheduledGap = IsScheduledGap(row);
            var time = row.TryGetProperty("actual_range", out var range) && range.ValueKind == JsonValueKind.Object &&
                range.TryGetProperty("end_s", out var end) ? Number(end) ?? double.NaN : double.NaN;
            if (row.TryGetProperty("output_values", out var outputs) && outputs.ValueKind == JsonValueKind.Object)
                foreach (var output in outputs.EnumerateObject())
                    yield return ($"{channel} · {output.Name}", unit, Number(output.Value), time, isScheduledGap);
            else if (id == "tnk_rbp")
                // Rejected RBP rows have no output_values; retain each band's gap identity.
                foreach (var band in new[] { "delta", "theta", "alpha", "beta" })
                    yield return ($"{channel} · {band}", unit, null, time, isScheduledGap);
            else
                yield return (channel, unit, row.TryGetProperty("value", out var scalar) ? Number(scalar) : null, time, isScheduledGap);
        }
    }

    private static bool IsScheduledGap(JsonElement row) =>
        row.TryGetProperty("evidence", out var evidence) && evidence.ValueKind == JsonValueKind.Object &&
        evidence.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object &&
        extensions.TryGetProperty("tnk_evidence", out var tnk) && tnk.ValueKind == JsonValueKind.Object &&
        tnk.TryGetProperty("provenance", out var provenance) && provenance.ValueKind == JsonValueKind.Object &&
        Text(provenance, "schedule_state") == "not_due";

    private static string Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static double? Number(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
}
