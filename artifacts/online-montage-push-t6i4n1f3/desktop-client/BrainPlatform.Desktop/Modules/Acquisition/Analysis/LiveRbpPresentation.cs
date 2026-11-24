using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

public sealed record LiveRbpChannelResult(string Channel, double?[] Shares, string Quality, string Warning, string Failure);
public sealed record LiveRbpUpdate(string State, string Message, double? StartSeconds, double? EndSeconds,
    string? ScientificIdentity, IReadOnlyList<LiveRbpChannelResult>? Channels = null,
    Guid? SessionId = null, long Revision = 0);

/// <summary>RBP owns its configuration and projection, outside the capture lifecycle.</summary>
internal static class LiveRbpPresentation
{
    internal const string AlgorithmId = "rbp";

    internal static JsonElement Config(IReadOnlyList<string> channels) => JsonSerializer.SerializeToElement(new
    {
        schema_version = "live-rbp-config-v1", channels, window_s = 10d, step_s = 1d, notch_hz = 50d,
    });

    internal static LiveRbpUpdate? Read(LiveAnalysisUpdate update, IReadOnlyList<string> channels)
    {
        if (!string.Equals(update.AlgorithmId, AlgorithmId, StringComparison.OrdinalIgnoreCase)) return null;
        if (update.Payload.ValueKind == JsonValueKind.Object)
        {
            try
            {
                var result = LiveRbpResultReader.Read(update.Payload, update.SessionId, channels, update.SamplingRateHz, update.Revision);
                if (result.State != update.State || result.StartSeconds != update.StartSeconds || result.EndSeconds != update.EndSeconds ||
                    result.ScientificIdentity != update.ScientificIdentity)
                    throw new InvalidOperationException("LIVE_RBP_RESULT_INVALID: 外层消息与 RBP 结果身份不一致。");
                return result;
            }
            catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException or JsonException or FormatException)
            {
                return Unavailable(update, channels, error.Message);
            }
        }
        if (update.State == "warming_up")
            return new("warming_up", "预热中：等待完整窗口", null, null, update.ScientificIdentity,
                SessionId: update.SessionId, Revision: update.Revision);
        return Unavailable(update, channels, update.Message ?? "当前实时窗口不可用。");
    }

    private static LiveRbpUpdate Unavailable(LiveAnalysisUpdate update, IReadOnlyList<string> channels, string message) =>
        new("unavailable", message, update.StartSeconds, update.EndSeconds, update.ScientificIdentity,
            channels.Select(channel => new LiveRbpChannelResult(channel, new double?[5], "unavailable", "", message)).ToArray(),
            update.SessionId, update.Revision);
}
