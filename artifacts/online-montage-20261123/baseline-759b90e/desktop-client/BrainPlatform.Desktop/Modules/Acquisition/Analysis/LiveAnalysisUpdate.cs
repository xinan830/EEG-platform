using System.Text.Json;
using BrainPlatform.Desktop.Modules.Acquisition.Runtime;

namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

/// <summary>Current live envelope; scientific payloads belong to their presenters.</summary>
public sealed record LiveAnalysisUpdate(Guid SessionId, string AlgorithmId, long Revision, string State,
    int SamplingRateHz, JsonElement Payload = default, double? StartSeconds = null, double? EndSeconds = null,
    string? ScientificIdentity = null, string? FailureCode = null, string? Message = null, int SkippedWindows = 0,
    long? StartSample = null, long? EndSample = null, long? MontageRevision = null)
{
    internal bool IsCurrentMontageResult(LiveMontageSnapshot montage) =>
        MontageRevision == montage.Revision;

    internal static LiveAnalysisUpdate Read(JsonElement value, Guid sessionId, int samplingRateHz)
    {
        string Text(string key) => value.GetProperty(key).GetString() ?? "";
        if (Text("type") != "live_analysis_update" || Text("schema_version") != "live-analysis-update-v2" ||
            !Guid.TryParse(Text("acquisition_session_id"), out var returnedSession) || returnedSession != sessionId)
            throw new InvalidOperationException("LIVE_ANALYSIS_UPDATE_INVALID: 实时算法消息版本或会话身份不匹配。");
        var algorithmId = Text("algorithm_id");
        var revision = value.GetProperty("revision").GetInt64();
        var state = Text("state");
        if (string.IsNullOrWhiteSpace(algorithmId) || revision < 0 ||
            state is not ("warming_up" or "ready" or "partial" or "unavailable" or "overloaded"))
            throw new InvalidOperationException("LIVE_ANALYSIS_UPDATE_INVALID: 实时算法身份或状态无效。");
        double? start = null, end = null;
        long? startSample = null, endSample = null;
        if (value.TryGetProperty("start_sample", out var left) && left.ValueKind != JsonValueKind.Null)
        {
            var first = left.GetInt64();
            var last = value.GetProperty("end_sample").GetInt64();
            start = value.GetProperty("start_s").GetDouble();
            end = value.GetProperty("end_s").GetDouble();
            if (first < 0 || last <= first || samplingRateHz <= 0 || !double.IsFinite(start.Value) ||
                !double.IsFinite(end.Value) || start < 0 || end <= start ||
                Math.Abs(end.Value - start.Value - (last - first) / (double)samplingRateHz) > 1e-6)
                throw new InvalidOperationException("LIVE_ANALYSIS_UPDATE_INVALID: 样本范围与时间范围不一致。");
            startSample = first;
            endSample = last;
        }
        var payload = value.TryGetProperty("payload", out var body) ? body.Clone() : default;
        var scientific = value.TryGetProperty("scientific_identity", out var identity) ? identity.GetString() : null;
        var montageRevision = value.TryGetProperty("montage_revision", out var montage) && montage.ValueKind != JsonValueKind.Null
            ? montage.GetInt64() : (long?)null;
        if (montageRevision < 0)
            throw new InvalidOperationException("LIVE_ANALYSIS_UPDATE_INVALID: 导联修订号无效。");
        var skipped = value.TryGetProperty("skipped_windows", out var skips) ? skips.GetInt32() : 0;
        string? code = null, message = null;
        if (value.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object)
        {
            code = failure.GetProperty("code").GetString();
            message = failure.GetProperty("message").GetString();
        }
        return new(sessionId, algorithmId, revision, state, samplingRateHz, payload, start, end, scientific, code, message, skipped,
            startSample, endSample, montageRevision);
    }
}
