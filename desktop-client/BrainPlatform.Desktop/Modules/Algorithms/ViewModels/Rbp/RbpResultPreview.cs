using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;

public sealed record RbpBandPoint(string Key, string Label, string Range, double? Share, string Color)
{
    public string ShareText => Share is double value ? $"{value:P1}" : "不可用";
}

public sealed record RbpWindowPoint(double TimeSeconds, double StartSeconds, double EndSeconds,
    string State, string Quality, string Failure, double?[] Shares);

public sealed class RbpResultPreview
{
    private static readonly (string Key, string Label, string Range, string Color)[] HistoricalBands =
    [
        ("delta", "δ Delta", "1–4 Hz", "#4D8BC8"),
        ("theta", "θ Theta", "4–8 Hz", "#46A78B"),
        ("alpha", "α Alpha", "8–13 Hz", "#D47B79"),
        ("beta", "β Beta", "13–30 Hz", "#C49A48"),
    ];
    private static readonly (string Key, string Label, string Range, string Color)[] CurrentBands =
    [
        .. HistoricalBands,
        ("gamma", "γ Gamma", "30–50 Hz", "#8D73D1"),
    ];

    private readonly (string Key, string Label, string Range, string Color)[] bands;

    private RbpResultPreview((string Key, string Label, string Range, string Color)[] bands,
        double?[] staticShares, RbpWindowPoint[] windows, string staticQuality, string staticFailure)
    {
        this.bands = bands;
        StaticShares = staticShares;
        Windows = windows;
        StaticQuality = staticQuality;
        StaticFailure = staticFailure;
    }

    public double?[] StaticShares { get; }
    public RbpWindowPoint[] Windows { get; }
    public string StaticQuality { get; }
    public string StaticFailure { get; }
    public bool IsDynamic => Windows.Length > 0;

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
        var bands = run?.ScientificVersion == "official-rbp-v2" ? CurrentBands : HistoricalBands;
        var unavailable = new double?[bands.Length];
        if (run?.Status != "completed" || run.ResultSummary is not { } summary ||
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
