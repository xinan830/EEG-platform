using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Latest-only coordinator for one review analysis provider.</summary>
public sealed class ReviewAnalysisCoordinator
{
    private readonly Func<string, long?, double?, CancellationToken, Task<WindowPreviewResponse>>? loadWindow;
    private readonly IReviewAlgorithmAdapter? provider;
    private long revision;
    private JsonElement? activeDynamicIdentity;

    public ReviewAnalysisCoordinator(Func<string, long?, double?, CancellationToken, Task<WindowPreviewResponse>> loadWindow)
        => this.loadWindow = loadWindow ?? throw new ArgumentNullException(nameof(loadWindow));

    public ReviewAnalysisCoordinator(IReviewAlgorithmAdapter provider)
        => this.provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public ReviewAnalysisContext? Context { get; private set; }
    public ReviewAnalysisSnapshot? CurrentSnapshot { get; private set; }
    public WindowPreviewResponse? CurrentWindow { get; private set; }
    public string AlgorithmDisplayName => provider?.DisplayName ?? "分析";
    public string AlgorithmId => provider?.AlgorithmId ?? "unknown";
    public bool SupportsOnDemandWindows => provider?.SupportsOnDemandWindows == true;

    public Task<AlgorithmCatalogItem> GetAlgorithmAsync(CancellationToken cancellationToken) =>
        (provider ?? throw new InvalidOperationException("未配置分析 provider。")) .GetAlgorithmAsync(cancellationToken);

    public Task<RegisteredRecording> RegisterAsync(string directory, CancellationToken cancellationToken,
        IReadOnlyList<MontageProfile>? montageProfiles = null) =>
        (provider ?? throw new InvalidOperationException("未配置分析 provider。"))
        .RegisterAsync(directory, cancellationToken, montageProfiles);

    public async Task<WindowPreviewResponse?> LoadOnDemandAtSampleAsync(
        ReviewAnalysisSnapshot snapshot, long cursorSample, CancellationToken cancellationToken,
        IProgress<DynamicWindowResponse>? progress = null)
    {
        if (provider is null || !provider.SupportsOnDemandWindows)
            throw new NotSupportedException("当前分析 adapter 未启用按需动态窗口。");
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(snapshot.AlgorithmId, provider.AlgorithmId, StringComparison.Ordinal))
            throw new ArgumentException("分析快照与当前 adapter 不匹配。", nameof(snapshot));
        if (snapshot.CursorSample != cursorSample)
            throw new ArgumentException("分析游标与不可变请求快照不匹配。", nameof(snapshot));
        if (cursorSample < 0) throw new ArgumentOutOfRangeException(nameof(cursorSample));

        var requestedRevision = Interlocked.Increment(ref revision);
        var result = await provider.LoadOnDemandAsync(snapshot.RecordingId, snapshot, cursorSample,
            cancellationToken, progress);
        if (requestedRevision != Volatile.Read(ref revision)) return null;
        ValidateWindowIdentity(snapshot, result);
        ValidateStableDynamicIdentity(snapshot, result);
        if (result.ScientificConfigurationJson is { } canonical &&
            result.BackendIdentity is { ValueKind: JsonValueKind.Object } resolvedIdentity &&
            resolvedIdentity.TryGetProperty("configuration_sha256", out var digest) && digest.ValueKind == JsonValueKind.String)
            snapshot = snapshot.BindScientificConfiguration(canonical, digest.GetString()!);
        else
            throw new InvalidOperationException("按需结果未能绑定完整科学配置快照，已拒绝绑定。");
        CurrentSnapshot = snapshot;
        Context = new ReviewAnalysisContext(snapshot.RecordingId, result.RunId, result.CursorSample, result.CursorSeconds);
        CurrentWindow = result;
        return result;
    }

    public Task<AnalysisRunResponse> EnsureRunAsync(
        AnalysisRunRequest request,
        Action<AnalysisRunResponse>? onStatusChanged,
        CancellationToken cancellationToken)
    {
        if (provider is null)
            throw new InvalidOperationException("当前协调器未配置可执行 provider。");
        return provider.EnsureRunAsync(request, onStatusChanged, cancellationToken);
    }

    public async Task<WindowPreviewResponse?> LoadAtSampleAsync(string recordingId, string runId, long cursorSample, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recordingId)) throw new ArgumentException("记录 ID 不能为空。", nameof(recordingId));
        if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("Run ID 不能为空。", nameof(runId));
        if (cursorSample < 0) throw new ArgumentOutOfRangeException(nameof(cursorSample));
        var requestedRevision = Interlocked.Increment(ref revision);
        var result = provider is not null
            ? await provider.LoadAsync(recordingId, runId, cursorSample, cancellationToken)
            : await (loadWindow ?? throw new InvalidOperationException("未配置分析 provider。"))(runId, cursorSample, null, cancellationToken);
        if (requestedRevision != Volatile.Read(ref revision)) return null;
        Context = new ReviewAnalysisContext(recordingId, runId, result.CursorSample, result.CursorSeconds);
        CurrentWindow = result;
        return result;
    }

    public async Task<WindowPreviewResponse?> LoadOnDemandAtSampleAsync(
        string recordingId,
        ReviewAnalysisSnapshot snapshot,
        long cursorSample,
        CancellationToken cancellationToken,
        IProgress<DynamicWindowResponse>? progress = null)
    {
        if (!string.Equals(snapshot.RecordingId, recordingId, StringComparison.Ordinal))
            throw new ArgumentException("分析快照与当前记录不匹配。", nameof(snapshot));
        return await LoadOnDemandAtSampleAsync(snapshot, cursorSample, cancellationToken, progress);
    }

    public Task ReleaseOnDemandAsync(CancellationToken cancellationToken = default) =>
        provider?.ReleaseOnDemandAsync(cancellationToken) ?? Task.CompletedTask;

    public void Reset()
    {
        Interlocked.Increment(ref revision);
        Context = null;
        CurrentWindow = null;
        CurrentSnapshot = null;
        activeDynamicIdentity = null;
        _ = ReleaseOnDemandAsync();
    }

    private static void ValidateWindowIdentity(ReviewAnalysisSnapshot snapshot, WindowPreviewResponse result)
    {
        if (!string.Equals(result.RecordingId, snapshot.RecordingId, StringComparison.Ordinal))
            throw new InvalidOperationException("算法结果记录身份与分析快照不匹配，已拒绝绑定。");
        if (result.Window is not { } window || window.StartSample < 0 || window.EndSample <= window.StartSample)
            throw new InvalidOperationException("按需结果缺少有效的后端实际样本范围，已拒绝绑定。");
        if (result.Status == "available" && result.CursorSample < window.EndSample)
            throw new InvalidOperationException("算法结果游标早于实际窗口结束位置，已拒绝绑定。");
        if (result.BackendIdentity is { ValueKind: JsonValueKind.Object } identity &&
            (!identity.TryGetProperty("start_sample", out var start) || !start.TryGetInt64(out var startSample) ||
             !identity.TryGetProperty("end_sample", out var end) || !end.TryGetInt64(out var endSample) ||
             startSample != window.StartSample || endSample != window.EndSample))
            throw new InvalidOperationException("后端身份范围与返回窗口实际样本范围不一致，已拒绝绑定。");
        if (result.ScientificVersion is { Length: > 0 } version &&
            snapshot.Configuration.TryGetProperty("scientific_version", out var configuredVersion) &&
            configuredVersion.ValueKind == System.Text.Json.JsonValueKind.String &&
            !string.Equals(version, configuredVersion.GetString(), StringComparison.Ordinal))
            throw new InvalidOperationException("算法结果版本与分析快照不匹配，已拒绝绑定。");
    }

    private void ValidateStableDynamicIdentity(ReviewAnalysisSnapshot snapshot, WindowPreviewResponse result)
    {
        if (result.BackendIdentity is not { ValueKind: JsonValueKind.Object } identity)
            throw new InvalidOperationException("按需结果缺少后端科学身份，已拒绝绑定。");
        var required = new[]
        {
            "recording_id", "source_sha256", "algorithm_id", "definition_id", "definition_version",
            "definition_sha256", "implementation_build", "scientific_version", "implementation_identity",
            "configuration_sha256", "window_policy_version",
        };
        foreach (var name in required)
        {
            if (!identity.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(field.GetString()))
                throw new InvalidOperationException($"按需结果缺少后端身份字段 {name}，已拒绝绑定。");
        }
        if (!Matches(identity, "recording_id", snapshot.RecordingId) ||
            !Matches(identity, "source_sha256", snapshot.SourceSha256) ||
            !Matches(identity, "algorithm_id", snapshot.AlgorithmId))
            throw new InvalidOperationException("后端科学身份与当前分析快照不匹配，已拒绝绑定。");
        if (snapshot.Configuration.TryGetProperty("scientific_version", out var configuredVersion) &&
            configuredVersion.ValueKind == JsonValueKind.String &&
            !Matches(identity, "scientific_version", configuredVersion.GetString()!))
            throw new InvalidOperationException("后端科学版本与分析配置不匹配，已拒绝绑定。");
        if (snapshot.DefinitionId is { } definitionId && !Matches(identity, "definition_id", definitionId) ||
            snapshot.DefinitionVersion is { } definitionVersion && !Matches(identity, "definition_version", definitionVersion) ||
            snapshot.ImplementationIdentity is { } implementation && !Matches(identity, "implementation_identity", implementation))
            throw new InvalidOperationException("后端 definition/implementation 身份与算法目录快照不匹配，已拒绝绑定。");

        if (activeDynamicIdentity is { } expected && !SameSessionIdentity(expected, identity))
            throw new InvalidOperationException("后端配置摘要、实现身份或窗口策略在同一分析快照内发生变化，已拒绝绑定。");
        activeDynamicIdentity ??= identity.Clone();
    }

    private static bool SameSessionIdentity(JsonElement left, JsonElement right)
    {
        // Per-window range and retry generation vary; the remaining fields are
        // the backend's stable scientific session identity.
        var fields = new[]
        {
            "project_scope", "recording_id", "source_sha256", "algorithm_id", "definition_id",
            "definition_version", "definition_sha256", "implementation_build", "scientific_version",
            "implementation_identity", "configuration_sha256", "window_policy_version",
        };
        return fields.All(name => PropertyText(left, name) == PropertyText(right, name));
    }

    private static string? PropertyText(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) ? property.ToString() : null;

    private static bool Matches(JsonElement value, string name, string expected) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
        string.Equals(property.GetString(), expected, StringComparison.Ordinal);
}
