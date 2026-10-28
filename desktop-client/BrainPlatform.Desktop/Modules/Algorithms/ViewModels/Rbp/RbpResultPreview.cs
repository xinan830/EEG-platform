using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;

public sealed record RbpBandPoint(string Key, string Label, string Range, double? Share, string Color)
{
    public string ShareText => Share is double value ? $"{value:P1}" : "不可用";
}

public sealed record RbpWindowPoint(double TimeSeconds, double StartSeconds, double EndSeconds,
    string State, string Quality, string Failure, double?[] Shares);

public sealed record RbpChannelWindowPoint(string Channel, RbpWindowPoint Window);

public sealed partial class RbpResultPreview
{
    private static readonly (string Key, string Label, string Range, string Color)[] CurrentBands =
    [
        ("delta", "δ Delta", "1–4 Hz", "#F04444"),
        ("theta", "θ Theta", "4–8 Hz", "#F6C744"),
        ("alpha", "α Alpha", "8–13 Hz", "#55A94F"),
        ("beta", "β Beta", "13–30 Hz", "#4C83B8"),
        ("gamma", "γ Gamma", "30–50 Hz", "#8D73D1"),
    ];

    private readonly (string Key, string Label, string Range, string Color)[] bands;

    private RbpResultPreview((string Key, string Label, string Range, string Color)[] bands,
        double?[] staticShares, RbpWindowPoint[] windows, string staticQuality, string staticFailure,
        IReadOnlyList<RbpChannelWindowPoint>? channelWindows = null)
    {
        this.bands = bands;
        StaticShares = staticShares;
        Windows = windows;
        StaticQuality = staticQuality;
        StaticFailure = staticFailure;
        ChannelWindows = channelWindows ?? [];
    }

    public double?[] StaticShares { get; }
    public RbpWindowPoint[] Windows { get; }
    public string StaticQuality { get; }
    public string StaticFailure { get; }
    public IReadOnlyList<RbpChannelWindowPoint> ChannelWindows { get; }
    public bool IsDynamic => Windows.Length > 0;

    public static IReadOnlyList<RbpBandPoint> BandDefinitions => CurrentBands.Select(band =>
        new RbpBandPoint(band.Key, band.Label, band.Range, null, band.Color)).ToArray();

    public static RbpResultPreview FromStructured(StructuredPreviewResponse preview)
    {
        if (preview.ScientificVersion != "official-rbp-v3" || preview.ChannelOrder is null || preview.ChannelOrder.Count == 0)
            throw new InvalidOperationException("RBP 结构化结果缺少 v3 导联顺序。");
        var channelWindows = new List<RbpChannelWindowPoint>();
        var timeAxis = ReadVector(preview.Axes, "time_s");
        var bandMatrices = CurrentBands.ToDictionary(band => band.Key, band => ReadMatrix(preview.Arrays, band.Key), StringComparer.Ordinal);
        var windowsByEnd = preview.Windows.GroupBy(window => Number(window, "end_s"))
            .ToDictionary(group => group.Key, group => group.First());
        var channelIndexValues = ReadVector(preview.Axes, "channel_index");
        if (channelIndexValues.Length != preview.ChannelOrder.Count || CurrentBands.Any(band =>
                bandMatrices[band.Key].Length != timeAxis.Length || bandMatrices[band.Key].Any(row => row.Length != preview.ChannelOrder.Count)))
            throw new InvalidOperationException("RBP 矩阵形状与窗口轴或导联顺序不一致。");
        for (var channel = 0; channel < preview.ChannelOrder.Count; channel++)
        for (var window = 0; window < timeAxis.Length; window++)
        {
            var shares = CurrentBands.Select(band => bandMatrices[band.Key][window][channel]).ToArray();
            if (shares.Any(value => value is null || value < 0 || value > 1)) shares = new double?[CurrentBands.Length];
            var end = timeAxis[window];
            var metadata = windowsByEnd.GetValueOrDefault(end);
            var start = metadata.ValueKind == JsonValueKind.Object ? Number(metadata, "start_s") : double.NaN;
            var state = metadata.ValueKind != JsonValueKind.Object ? "Complete" : String(metadata, "state");
            var quality = metadata.ValueKind != JsonValueKind.Object ? "clean" : String(metadata, "quality");
            if (state is "Rejected" or "Unavailable" || quality is "failed" or "unavailable") shares = new double?[CurrentBands.Length];
            channelWindows.Add(new RbpChannelWindowPoint(preview.ChannelOrder[channel], new RbpWindowPoint(end, start, end,
                state, quality, metadata.ValueKind != JsonValueKind.Object ? "" : FailureText(metadata), shares)));
        }
        var primary = preview.ChannelOrder[0];
        var points = channelWindows.Where(item => item.Channel == primary).Select(item => item.Window).OrderBy(item => item.EndSeconds).ToArray();
        return new RbpResultPreview(CurrentBands, points.LastOrDefault()?.Shares ?? new double?[CurrentBands.Length], points,
            preview.Quality?.ToString() ?? "", "", channelWindows);
    }

    private static double[] ReadVector(IReadOnlyDictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"RBP 结构化结果缺少 {key} 轴。");
        return value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var number) ? number : double.NaN).ToArray();
    }

    private static double?[][] ReadMatrix(IReadOnlyDictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"RBP 结构化结果缺少 {key} 矩阵。");
        return value.EnumerateArray().Select(row => row.ValueKind == JsonValueKind.Array
            ? row.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var number) && double.IsFinite(number) ? (double?)number : null).ToArray()
            : []).ToArray();
    }

    private static string FailureText(JsonElement window) =>
        window.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object
            ? $"{String(failure, "code")}：{String(failure, "message")}" : "";

    public IReadOnlyList<RbpBandPoint> BandsAt(double cursorSeconds)
    {
        var shares = IsDynamic
            ? Windows.LastOrDefault(window => window.EndSeconds <= cursorSeconds + 1e-9)?.Shares
            : StaticShares;
        return bands.Select((band, index) => new RbpBandPoint(band.Key, band.Label, band.Range,
            shares is not null ? shares[index] : null, band.Color)).ToArray();
    }

    public static RbpResultPreview Parse(AnalysisRunResponse? run)
    {
        if (run is not null) CurrentAlgorithmResult.Require(run);
        var isCurrentVersion = run?.ScientificVersion is "official-rbp-v2" or "official-rbp-v3";
        var bands = isCurrentVersion ? CurrentBands : [];
        var unavailable = new double?[bands.Length];
        if (!isCurrentVersion || run?.Status != "completed" || run.ResultSummary is not { } summary ||
            !summary.TryGetProperty("metric", out var metric) || metric.ValueKind != JsonValueKind.Object)
            return new RbpResultPreview(bands, unavailable, [], "", "");

        if (metric.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
        {
            var windows = series.EnumerateArray().Where(point => point.ValueKind == JsonValueKind.Object).Select(point =>
            {
                var state = String(point, "analysis_state");
                var quality = point.TryGetProperty("quality", out var qualityValue) && qualityValue.ValueKind == JsonValueKind.Object
                    ? String(qualityValue, "status") : "unavailable";
                var failure = point.TryGetProperty("quality", out qualityValue) && qualityValue.ValueKind == JsonValueKind.Object &&
                    qualityValue.TryGetProperty("reasons", out var reasons) && reasons.ValueKind == JsonValueKind.Array
                    ? string.Join("、", reasons.EnumerateArray().Where(reason => reason.ValueKind == JsonValueKind.String).Select(reason => reason.GetString())) : "";
                var usable = (state is "Complete" or "Partial") && quality == "clean";
                return new RbpWindowPoint(Number(point, "time_s"), Number(point, "window_start_s"),
                    Number(point, "window_end_s"), state, quality, failure,
                    usable && point.TryGetProperty("band_values", out var values) ? ReadShares(values, bands) : new double?[bands.Length]);
            }).Where(point => double.IsFinite(point.TimeSeconds) && double.IsFinite(point.EndSeconds))
              .OrderBy(point => point.EndSeconds).ToArray();
            return new RbpResultPreview(bands, unavailable, windows, "", "");
        }

        var clean = metric.TryGetProperty("quality", out var staticQuality) && staticQuality.ValueKind == JsonValueKind.Object &&
            String(staticQuality, "status") == "clean";
        var failure = metric.TryGetProperty("failure", out var staticFailure) && staticFailure.ValueKind == JsonValueKind.Object
            ? String(staticFailure, "message") : "";
        return new RbpResultPreview(bands, clean && metric.TryGetProperty("band_values", out var staticValues)
            ? ReadShares(staticValues, bands) : unavailable, [],
            staticQuality.ValueKind == JsonValueKind.Object ? String(staticQuality, "status") : "", failure);
    }

    private static double?[] ReadShares(JsonElement values, (string Key, string Label, string Range, string Color)[] bands)
    {
        if (values.ValueKind != JsonValueKind.Object || bands.Any(band => !values.TryGetProperty(band.Key, out _)))
            return new double?[bands.Length];
        var shares = bands.Select(band =>
        {
            if (!values.TryGetProperty(band.Key, out var value) ||
                value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) ||
                !double.IsFinite(number) || number < 0 || number > 1)
                return (double?)null;
            return number;
        }).ToArray();
        // A current five-band RBP result is one normalized measurement set.
        // Never render a partial set if any returned share is missing/invalid.
        return bands.Length == CurrentBands.Length && shares.Any(share => share is null)
            ? new double?[bands.Length]
            : shares;
    }

    private static string String(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? "" : "";

    private static double Number(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.Number &&
        property.TryGetDouble(out var number) ? number : double.NaN;
}
