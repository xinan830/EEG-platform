using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;

namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

/// <summary>Validates the live HTTP contract without inventing an offline Run identity.</summary>
internal static class LiveRbpResultReader
{
    internal static LiveRbpUpdate Read(JsonElement result, Guid sessionId,
        IReadOnlyList<string> expectedChannels, int samplingRateHz, long revision)
    {
        if (Text(result, "type") != "live_rbp_result" || Text(result, "schema_version") != "live-rbp-result-v1" ||
            Text(result, "algorithm_id") != "rbp" || Text(result, "scientific_identity") != "rbp-live-v1" ||
            !Guid.TryParse(Text(result, "acquisition_session_id"), out var returnedSession) || returnedSession != sessionId)
            throw Invalid("算法类型、版本或会话身份不匹配");
        var state = Text(result, "state");
        if (state is not ("ready" or "partial" or "unavailable")) throw Invalid("窗口状态无效");
        var channels = result.GetProperty("channels").EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
        if (!channels.SequenceEqual(expectedChannels, StringComparer.OrdinalIgnoreCase) || channels.Length == 0 ||
            channels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != channels.Length)
            throw Invalid("通道顺序不匹配");
        var startSample = result.GetProperty("start_sample").GetInt64();
        var endSample = result.GetProperty("end_sample").GetInt64();
        var start = result.GetProperty("start_s").GetDouble();
        var end = result.GetProperty("end_s").GetDouble();
        if (startSample < 0 || endSample <= startSample || samplingRateHz <= 0 ||
            !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start ||
            Math.Abs(end - start - (endSample - startSample) / (double)samplingRateHz) > 1e-6)
            throw Invalid("样本范围与时间范围不一致");
        var values = result.GetProperty("values");
        var units = result.GetProperty("units");
        var quality = result.GetProperty("quality");
        var bands = RbpResultPreview.BandDefinitions.Select(band => band.Key).ToArray();
        var matrices = bands.Select(band =>
        {
            if (Text(units, band) != "ratio") throw Invalid("相对功率单位必须是 ratio");
            var vector = values.GetProperty(band).EnumerateArray().Select(item =>
            {
                if (item.ValueKind == JsonValueKind.Null) return (double?)null;
                var value = item.GetDouble();
                if (!double.IsFinite(value) || value < 0 || value > 1) throw Invalid("频段数值无效");
                return (double?)value;
            }).ToArray();
            if (vector.Length != channels.Length) throw Invalid("频段数组与通道数不匹配");
            return vector;
        }).ToArray();
        var projected = channels.Select((channel, index) =>
        {
            var details = quality.GetProperty(channel);
            var channelState = Text(details, "status");
            if (channelState is not ("clean" or "warning" or "partial" or "unavailable"))
                throw Invalid("通道质量状态无效");
            var reasons = details.TryGetProperty("reasons", out var reasonValues)
                ? string.Join("、", reasonValues.EnumerateArray().Select(item => item.GetString())) : "";
            var shares = matrices.Select(vector => vector[index]).ToArray();
            if (state == "unavailable" || channelState == "unavailable" || shares.Any(value => value is null))
            {
                Array.Fill(shares, null);
                channelState = "unavailable";
            }
            return new LiveRbpChannelResult(channel, shares, channelState,
                channelState is "warning" or "partial" ? reasons : "",
                channelState == "unavailable" ? (reasons.Length > 0 ? reasons : "当前通道无有效五频段结果") : "");
        }).ToArray();
        var message = result.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object
            ? Text(failure, "message") : "相对功率（%）";
        return new LiveRbpUpdate(state, message, start, end, "rbp-live-v1", projected, sessionId, revision);
    }

    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? "";
    private static InvalidOperationException Invalid(string reason) => new($"LIVE_RBP_RESULT_INVALID: {reason}。");
}
