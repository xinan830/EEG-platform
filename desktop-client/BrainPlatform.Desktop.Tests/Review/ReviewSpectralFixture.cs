using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

internal static class ReviewSpectralFixture
{
    internal static readonly string[] ChannelNames = ["F3", "F7"];
    internal static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    internal static WindowPreviewResponse Window(string algorithm, long cursor = 10000,
        JsonElement? config = null, string[]? channels = null, double overlap = 0.5,
        string quality = "clean", string filter = "same", string source = "sha")
    {
        channels ??= ChannelNames;
        var canonical = JsonSerializer.Serialize(new
        {
            scientific_configuration_contract = "scientific-config-v1",
            config = config ?? Json(new { algorithm_id = algorithm }),
            context = new
            {
                sampling_rate_hz = 500,
                signal_mapping = new { channels, analysis_montage = "montage" },
                reference = new { mode = "acquisition" }, filters = new { type = filter },
                window = new { welch_segment_s = 4, welch_overlap = overlap, dynamic_window_s = 10,
                    refresh_step_s = 1, alignment = "window_end" },
            },
        });
        var identity = Json(new
        {
            recording_id = "registered", source_sha256 = source, algorithm_id = algorithm,
            definition_id = "definition", definition_version = "1", definition_sha256 = "def-sha",
            implementation_build = "build", scientific_version = "fixture-v1", implementation_identity = "impl",
            configuration_sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant(),
            window_policy_version = "policy", start_sample = cursor - 5000, end_sample = cursor,
        });
        var arrays = new Dictionary<string, JsonElement>();
        var metadata = new Dictionary<string, JsonElement>();
        foreach (var band in ReviewSpectralSources.Bands)
        {
            arrays[band] = Json(channels.Select((_, index) => index == 0 ? 0.25 : 0.5));
            arrays[band + "_absolute_power"] = Json(channels.Select((_, index) => index == 0 ? 4d : 9));
            metadata[band] = Json(new { unit = "ratio" });
            metadata[band + "_absolute_power"] = Json(new { unit = "uV^2" });
        }
        return new WindowPreviewResponse("session-" + algorithm, "registered", cursor, cursor / 500d,
            "available", [], new WindowPreviewWindow($"{algorithm}-{cursor}", 0, cursor - 5000, cursor,
                "Ready", quality, []), false, null, null, null, null, arrays, metadata, "fixture-v1", "build",
            identity, canonical, Json(new { extensions = new
            {
                channel_order = new { channels }, channel_quality = new { by_channel = channels.ToDictionary(
                    channel => channel, _ => new { status = quality, reasons = Array.Empty<string>() }) },
            }}), null, (cursor - 5000) / 500d, cursor / 500d);
    }

    internal static ResultScalarsResponse Projection(WindowPreviewResponse window, string algorithm,
        string[]? channels = null)
    {
        using var configuration = JsonDocument.Parse(window.ScientificConfigurationJson!);
        channels ??= configuration.RootElement.GetProperty("context").GetProperty("signal_mapping").GetProperty("channels")
            .EnumerateArray().Select(item => item.GetString()!).ToArray();
        var prefix = algorithm == "psd" ? "psd.band-mean-density." : "rbp.amplitude.";
        var unit = algorithm == "psd" ? "V^2/Hz" : "uV";
        return new ResultScalarsResponse(window.Window!.WindowId, window.RecordingId, algorithm,
            "fixture-v1", "build", window.WindowStartSeconds!.Value, window.WindowEndSeconds!.Value, "ready",
            ReviewSpectralSources.Bands.Select((band, bandIndex) => new ResultScalarField(prefix + band,
                band, unit, channels.Select((channel, index) => new ResultScalarPoint(channel,
                    algorithm == "psd" ? (bandIndex + index + 1) * 1e-12 : index == 0 ? 2 : 3,
                    "clean", [])).ToArray(), "ready", null, null)).ToArray(), null);
    }
}
