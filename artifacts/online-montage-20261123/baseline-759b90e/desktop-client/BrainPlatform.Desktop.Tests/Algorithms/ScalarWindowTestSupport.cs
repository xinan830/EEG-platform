using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Algorithms;

internal static class ScalarWindowTestSupport
{
    internal static (string Field, string Unit, string EvidenceName) Schema(string id) => id switch
    {
        "band_ratio" => ("ratio", "ratio", "band_ratio_evidence"),
        "peak_frequency" => ("peak_frequency_hz", "Hz", "peak_frequency_evidence"),
        "iapf" => ("iapf_hz", "Hz", "iapf_evidence"),
        "theta_beta" => ("theta_beta", "dimensionless", "theta_beta_evidence"),
        _ => throw new ArgumentException(id),
    };

    internal static DynamicWindowResponse Response(DynamicWindowRequest request)
    {
        var end = request.CursorSample / 200d;
        var start = Math.Max(0, end - 5);
        var ready = end >= 4;
        var (field, unit, evidenceName) = Schema(request.AlgorithmId);
        var specific = request.AlgorithmId switch
        {
            "band_ratio" => Json(new { numerator_band_hz = new[] { 8, 12 }, denominator_band_hz = new[] { 15, 25 },
                numerator_power_v2 = 2e-12, denominator_power_v2 = 4e-12 }),
            "peak_frequency" => Json(new { selected_power_v2_per_hz = 3e-12 }),
            "iapf" => Json(new { source = "peak", peak_hz = 10, cog_hz = 9.9, model_r2 = 0.95 }),
            _ => Json(new { iapf_hz = 10, theta_range_hz = new[] { 4, 7 }, beta_range_hz = new[] { 11, 30 },
                theta_power_uv2 = 2, beta_power_uv2 = 4 }),
        };
        var result = new DynamicWindowResultResponse(null, "ready", "scalar_series",
            new Dictionary<string, JsonElement> { ["time_s"] = Json(new[] { end }) },
            new Dictionary<string, string> { ["time_s"] = "s" },
            new Dictionary<string, JsonElement> { [field] = Json(new[] { 0.5 }) },
            new Dictionary<string, string> { [field] = unit }, "clean", null,
            Json(new { extensions = new Dictionary<string, JsonElement> { [evidenceName] = specific },
                calculation_trace = new { formula = "backend formula" } }),
            Json(new { analysis_state = end < 5 ? "Partial" : "Complete",
                preprocessing_scope = "whole_recording_before_window_slice" }));
        var insufficientStart = request.CursorSample;
        return new DynamicWindowResponse("session-" + request.Config.GetRawText(), "window-" + request.CursorSample,
            request.RequestId ?? "request", ready ? "ready" : "insufficient_data",
            Json(new { recording_id = request.RecordingId }), null,
            ready ? new DynamicWindowRange((long)(start * 200), request.CursorSample, start, end)
                : new DynamicWindowRange(insufficientStart, insufficientStart + 1, end, end + 0.005),
            ready ? "clean" : "insufficient_data",
            ready ? null : new DynamicWindowFailure("WINDOW_INSUFFICIENT_DATA", "数据不足"),
            ready ? result : null, "subscription");
    }

    internal static IAlgorithmQualitySource Module(AlgorithmDetailViewModel detail, string id) => id switch
    {
        "band_ratio" => detail.BandRatio, "peak_frequency" => detail.PeakFrequency,
        "iapf" => detail.Iapf, "theta_beta" => detail.ThetaBeta, _ => throw new ArgumentException(id),
    };

    internal static object? Point(IAlgorithmQualitySource module) =>
        module.GetType().GetProperty("CurrentPoint")!.GetValue(module);

    internal static IReadOnlyList<object> Points(IAlgorithmQualitySource module) =>
        ((System.Collections.IEnumerable)module.GetType().GetProperty("TrendPoints")!.GetValue(module)!).Cast<object>().ToArray();

    internal static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;

    internal static double? Value(object point) => (double?)(point.GetType().GetProperty("Value") ??
        point.GetType().GetProperty("ValueHz"))!.GetValue(point);

    internal static DynamicWindowRequest Request(string id, long cursor = 1200) =>
        new("recording", id, null, null, Json(new { channel = "F3" }), cursor, RequestId: "request");

    internal static object Preview(string id, DynamicWindowResponse response, object? previous = null) => id switch
    {
        "band_ratio" => BandRatioResultPreview.FromWindow(response, "F3", previous as BandRatioResultPreview),
        "peak_frequency" => PeakFrequencyResultPreview.FromWindow(response, "F3", previous as PeakFrequencyResultPreview),
        "iapf" => IapfResultPreview.FromWindow(response, "F3", previous as IapfResultPreview),
        "theta_beta" => ThetaBetaResultPreview.FromWindow(response, "F3", previous as ThetaBetaResultPreview),
        _ => throw new ArgumentException(id),
    };

    internal static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
}
