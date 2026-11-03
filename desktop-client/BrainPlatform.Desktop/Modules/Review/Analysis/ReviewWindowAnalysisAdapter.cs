using System.Security.Cryptography;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Formula-free Review adapter for a declared dynamic-window algorithm.</summary>
public sealed class ReviewWindowAnalysisAdapter : IReviewAlgorithmAdapter
{
    private readonly IAlgorithmClient client;
    private readonly bool? enableOnDemand;
    private readonly DynamicWindowTargetCoordinator targets;

    public ReviewWindowAnalysisAdapter(IAlgorithmClient client, string algorithmId, string displayName,
        bool? enableOnDemand = null)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithmId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        AlgorithmId = algorithmId;
        DisplayName = displayName;
        this.enableOnDemand = enableOnDemand;
        targets = new DynamicWindowTargetCoordinator(client);
    }

    public string AlgorithmId { get; }
    public string DisplayName { get; }
    public bool SupportsOnDemandWindows =>
        enableOnDemand ?? OfflineAnalysisAvailability.IsOnDemandEnabled(AlgorithmId, enabledByDefault: true);

    public async Task<AlgorithmCatalogItem> GetAlgorithmAsync(CancellationToken cancellationToken)
    {
        var algorithm = (await client.ListAlgorithmsAsync(cancellationToken))
            .SingleOrDefault(item => item.Id == AlgorithmId);
        if (algorithm is null || !algorithm.IsRunnable || !algorithm.SupportsDeclaredOnDemand)
            throw new NotSupportedException($"当前 {DisplayName} 未声明可运行的按需动态窗口能力。");
        return algorithm;
    }

    public Task<RegisteredRecording> RegisterAsync(string directory, CancellationToken cancellationToken,
        IReadOnlyList<MontageProfile>? montageProfiles = null) =>
        (client as IRecordingRegistrationClient ?? throw new InvalidOperationException("算法客户端不支持记录注册。"))
        .RegisterWpfRecordingAsync(directory, cancellationToken, montageProfiles);

    public Task<AnalysisRunResponse> EnsureRunAsync(AnalysisRunRequest request,
        Action<AnalysisRunResponse>? onStatusChanged, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Review 算法面板只使用按需窗口，不为游标创建完整 Run。");

    public Task<WindowPreviewResponse> LoadAsync(string recordingId, string runId, long cursorSample,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Review 算法面板只使用按需窗口。");

    public Task<WindowPreviewResponse> LoadOnDemandAsync(string recordingId, AnalysisRunRequest request,
        long cursorSample, CancellationToken cancellationToken) =>
        LoadOnDemandAsync(recordingId, request, cursorSample, cancellationToken, null);

    public async Task<WindowPreviewResponse> LoadOnDemandAsync(string recordingId, AnalysisRunRequest request,
        long cursorSample, CancellationToken cancellationToken, IProgress<DynamicWindowResponse>? progress)
    {
        ValidateRequest(recordingId, request, cursorSample);
        var response = await targets.LoadAsync(new DynamicWindowRequest(
            recordingId, AlgorithmId, null, null, request.Config, cursorSample,
            RequestId: Guid.NewGuid().ToString("N")), cancellationToken, progress);
        return ToWindowPreview(response, recordingId, cursorSample);
    }

    public async Task<WindowPreviewResponse> LoadOnDemandAsync(string recordingId,
        ReviewAnalysisSnapshot snapshot, long cursorSample, CancellationToken cancellationToken,
        IProgress<DynamicWindowResponse>? progress)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.AlgorithmId != AlgorithmId || snapshot.RecordingId != recordingId)
            throw new ArgumentException("分析快照与当前回溯记录不匹配。", nameof(snapshot));
        if (cursorSample < 0) throw new ArgumentOutOfRangeException(nameof(cursorSample));

        var response = await targets.LoadAsync(new DynamicWindowRequest(
            recordingId, AlgorithmId, snapshot.DefinitionId, snapshot.DefinitionVersion,
            snapshot.Configuration, cursorSample, RequestId: Guid.NewGuid().ToString("N")),
            cancellationToken, progress);
        ValidateSnapshotIdentity(response, snapshot);
        return ToWindowPreview(response, recordingId, cursorSample);
    }

    public Task ReleaseOnDemandAsync(CancellationToken cancellationToken = default) =>
        targets.ResetAsync().WaitAsync(cancellationToken);

    public Task<TopomapResponse> LoadTopomapAsync(WindowPreviewResponse window,
        CancellationToken cancellationToken, string powerType = "relative", string? bandId = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.DynamicResponse is { } dynamic)
            return client.GetDynamicTopomapAsync(dynamic.WindowId, null, cancellationToken, powerType, bandId);
        return client.GetRunTopomapAsync(window.RunId, window.CursorSample, cancellationToken, powerType, bandId);
    }

    private void ValidateRequest(string recordingId, AnalysisRunRequest request, long cursorSample)
    {
        if (!SupportsOnDemandWindows)
            throw new NotSupportedException($"{DisplayName} 未启用回溯按需适配。");
        if (string.IsNullOrWhiteSpace(recordingId) || request.RecordingId != recordingId)
            throw new ArgumentException("配置不属于当前回溯记录。", nameof(request));
        if (cursorSample < 0) throw new ArgumentOutOfRangeException(nameof(cursorSample));
        if (!request.Config.TryGetProperty("algorithm_id", out var algorithm) ||
            algorithm.GetString() != AlgorithmId)
            throw new ArgumentException("按需窗口请求与面板算法不匹配。", nameof(request));
    }

    internal static WindowPreviewResponse ToWindowPreview(DynamicWindowResponse response,
        string recordingId, long requestedCursor)
    {
        if (response.Identity is not { } identity ||
            !identity.TryGetProperty("recording_id", out var source) || source.GetString() != recordingId)
            throw new InvalidOperationException("按需结果不属于当前回溯记录，已拒绝绑定。");

        var plan = response.Plan;
        var range = response.Range;
        var startSample = plan?.StartSample ?? range?.StartSample ?? 0;
        var endSample = plan?.EndSample ?? range?.EndSample ?? startSample;
        var cursorSample = plan?.CursorSample ?? requestedCursor;
        var endSeconds = plan?.EndSeconds ?? range?.EndSeconds ?? 0;
        var samplingRate = endSeconds > 0 ? endSample / endSeconds : 1;
        var cursorSeconds = plan?.CursorSeconds ?? (samplingRate > 0 ? cursorSample / samplingRate : 0);
        var failure = response.Failure is { } outerFailure
            ? JsonSerializer.SerializeToElement(outerFailure) : response.Result?.Failure;
        var reason = failure is { ValueKind: JsonValueKind.Object } detail &&
            detail.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null;
        var reasons = reason is null ? Array.Empty<string>() : new[] { reason };
        var state = response.State switch
        {
            "ready" => "available",
            "insufficient_data" => "insufficient_data",
            "rejected" => "rejected",
            "cancelled" => "cancelled",
            "failed" => "failed",
            "unavailable" => "unavailable",
            _ => response.State,
        };
        var result = response.Result;
        var metadata = result?.ArrayUnits?.ToDictionary(
            item => item.Key,
            item => JsonSerializer.SerializeToElement(new { unit = item.Value }))
            ?? new Dictionary<string, JsonElement>();
        return new WindowPreviewResponse(
            response.SessionId, recordingId, cursorSample, cursorSeconds, state, reasons,
            new WindowPreviewWindow(response.WindowId, 0, startSample, endSample,
                response.State, response.Quality ?? "unknown", reasons, result?.Provenance), false,
            result is null ? null : JsonSerializer.SerializeToElement(new { kind = result.OutputKind }),
            JsonSerializer.SerializeToElement(new { status = result?.Quality ?? response.Quality,
                state = result?.State ?? response.State, failure }),
            result?.Axes, result?.AxisUnits?.ToDictionary(item => item.Key,
                item => JsonSerializer.SerializeToElement(new { unit = item.Value })), result?.Arrays, metadata,
            identity.TryGetProperty("scientific_version", out var science) ? science.GetString() : null,
            identity.TryGetProperty("implementation_identity", out var implementation) ? implementation.GetString() : null,
            identity.Clone(), response.ScientificConfigurationJson, result?.Evidence, result?.Provenance,
            plan?.StartSeconds ?? range?.StartSeconds, plan?.EndSeconds ?? range?.EndSeconds)
        {
            DynamicResponse = response,
        };
    }

    internal static void ValidateSnapshotIdentity(DynamicWindowResponse response, ReviewAnalysisSnapshot snapshot)
    {
        if (response.Identity is not { ValueKind: JsonValueKind.Object } identity ||
            !Matches(identity, "recording_id", snapshot.RecordingId) ||
            !Matches(identity, "source_sha256", snapshot.SourceSha256) ||
            !Matches(identity, "algorithm_id", snapshot.AlgorithmId))
            throw new InvalidOperationException("按需响应身份与分析快照不一致，已拒绝绑定。");
        var canonical = response.ScientificConfigurationJson;
        if (string.IsNullOrWhiteSpace(canonical) ||
            !identity.TryGetProperty("configuration_sha256", out var digest) ||
            digest.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("按需响应缺少可核验的科学配置摘要，已拒绝绑定。");
        _ = snapshot.BindScientificConfiguration(canonical, digest.GetString()!);
    }

    private static bool Matches(JsonElement identity, string name, string expected) =>
        identity.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString() == expected;
}
