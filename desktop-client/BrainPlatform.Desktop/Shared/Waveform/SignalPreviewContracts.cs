using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Shared.Waveform;

public sealed record SignalPreviewRequest(
    [property: JsonPropertyName("start_s")] double StartSeconds,
    [property: JsonPropertyName("window_s")] double WindowSeconds,
    [property: JsonPropertyName("montage_id")] string? MontageId,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels,
    [property: JsonPropertyName("max_points")] int MaxPoints = 4000,
    [property: JsonPropertyName("low_hz")] double LowHz = 1,
    [property: JsonPropertyName("high_hz")] double HighHz = 30,
    [property: JsonPropertyName("notch_hz")] double? NotchHz = null);

public sealed record SignalPreviewResponse(
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("montage_id")] string? MontageId,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("filter")] string Filter,
    [property: JsonPropertyName("elapsed_s")] IReadOnlyList<double> TimeSeconds,
    [property: JsonPropertyName("channels")] IReadOnlyDictionary<string, double[]> Channels,
    [property: JsonPropertyName("low_hz")] double LowHz = 1,
    [property: JsonPropertyName("high_hz")] double HighHz = 30,
    [property: JsonPropertyName("notch_hz")] double? NotchHz = null,
    [property: JsonPropertyName("extrema_elapsed_s")] IReadOnlyList<double>? ExtremaTimeSeconds = null,
    [property: JsonPropertyName("channel_extrema")] IReadOnlyDictionary<string, SignalPreviewChannelExtrema>? ChannelExtrema = null);

public sealed record SignalPreviewChannelExtrema(
    [property: JsonPropertyName("minimum_uv")] IReadOnlyList<double> MinimumMicrovolts,
    [property: JsonPropertyName("maximum_uv")] IReadOnlyList<double> MaximumMicrovolts);
