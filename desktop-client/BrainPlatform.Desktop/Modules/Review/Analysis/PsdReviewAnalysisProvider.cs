using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>PSD's review adapter. Scientific values remain backend-owned.</summary>
public sealed class PsdReviewAnalysisProvider : IReviewAnalysisProvider
{
    private static readonly TimeSpan RunPollInterval = TimeSpan.FromMilliseconds(500);
    private readonly IAlgorithmClient client;
    private readonly bool? enableOnDemand;
    private readonly DynamicWindowTargetCoordinator targets;

    public PsdReviewAnalysisProvider(IAlgorithmClient client, bool? enableOnDemand = null)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.enableOnDemand = enableOnDemand;
        targets = new DynamicWindowTargetCoordinator(client);
    }
    public string AlgorithmId => "psd";

    // Review and the algorithm catalog share the accepted PSD availability policy.
    public bool SupportsOnDemandWindows =>
        enableOnDemand ?? OfflineAnalysisAvailability.IsOnDemandEnabled(AlgorithmId, enabledByDefault: true);

    public async Task<AlgorithmCatalogItem> GetAlgorithmAsync(CancellationToken cancellationToken) =>
        (await client.ListAlgorithmsAsync(cancellationToken)).Single(item => item.Id == AlgorithmId && item.IsRunnable);

    public Task<RegisteredRecording> RegisterAsync(string directory, CancellationToken cancellationToken) =>
        (client as IRecordingRegistrationClient ?? throw new InvalidOperationException("算法客户端不支持记录注册。"))
        .RegisterWpfRecordingAsync(directory, cancellationToken);

    public async Task<AnalysisRunResponse> EnsureRunAsync(
        AnalysisRunRequest request,
        Action<AnalysisRunResponse>? onStatusChanged,
        CancellationToken cancellationToken)
    {
        var run = await client.EnsureRunAsync(request, cancellationToken);
        onStatusChanged?.Invoke(run);
        while (run.Status is "queued" or "running")
        {
            await Task.Delay(RunPollInterval, cancellationToken);
            run = await client.GetRunAsync(run.RunId, cancellationToken);
            onStatusChanged?.Invoke(run);
        }
        return run;
    }

    public async Task<WindowPreviewResponse> LoadAsync(
        string recordingId,
        string runId,
        long cursorSample,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recordingId))
            throw new ArgumentException("记录 ID 不能为空。", nameof(recordingId));
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("Run ID 不能为空。", nameof(runId));
        if (cursorSample < 0)
            throw new ArgumentOutOfRangeException(nameof(cursorSample));

        var result = await client.GetWindowPreviewAsync(runId, cursorSample, null, cancellationToken);
        if (!string.Equals(result.RecordingId, recordingId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("算法结果不属于当前回溯记录，已拒绝绑定。");
        }

        return result;
    }

    public Task<WindowPreviewResponse> LoadOnDemandAsync(
        string recordingId,
        AnalysisRunRequest request,
        long cursorSample,
        CancellationToken cancellationToken) =>
        LoadOnDemandAsync(recordingId, request, cursorSample, cancellationToken, null);

    public async Task<WindowPreviewResponse> LoadOnDemandAsync(string recordingId,
        AnalysisRunRequest request, long cursorSample, CancellationToken cancellationToken,
        IProgress<DynamicWindowResponse>? progress)
    {
        if (!SupportsOnDemandWindows)
            throw new NotSupportedException("按需 PSD 尚未启用；请使用完整动态 Run 路径。");
        if (string.IsNullOrWhiteSpace(recordingId)) throw new ArgumentException("记录 ID 不能为空。", nameof(recordingId));
        if (cursorSample < 0) throw new ArgumentOutOfRangeException(nameof(cursorSample));
        if (!request.Config.TryGetProperty("algorithm_id", out var algorithm) ||
            !string.Equals(algorithm.GetString(), AlgorithmId, StringComparison.Ordinal))
            throw new ArgumentException("按需窗口请求必须是 PSD 配置。", nameof(request));

        var dynamicRequest = new DynamicWindowRequest(
            request.RecordingId,
            AlgorithmId,
            null,
            null,
            request.Config,
            cursorSample,
            null,
            Guid.NewGuid().ToString("N"));
        if (request.RecordingId != recordingId)
            throw new ArgumentException("按需配置不属于当前回溯记录。", nameof(request));
        var response = await targets.LoadAsync(dynamicRequest, cancellationToken, progress);
        return ToWindowPreview(response, recordingId, cursorSample);
    }

    public Task ReleaseOnDemandAsync(CancellationToken cancellationToken = default) =>
        targets.ResetAsync().WaitAsync(cancellationToken);

    private static WindowPreviewResponse ToWindowPreview(
        DynamicWindowResponse response,
        string recordingId,
        long requestedCursor)
    {
        var plan = response.Plan;
        var range = response.Range;
        var startSample = plan?.StartSample ?? range?.StartSample ?? 0;
        var endSample = plan?.EndSample ?? range?.EndSample ?? startSample;
        if (response.Identity is not { } identity ||
            !identity.TryGetProperty("recording_id", out var source) || source.GetString() != recordingId)
            throw new InvalidOperationException("按需结果不属于当前回溯记录，已拒绝绑定。");
        // Keep the backend-resolved cursor as the displayed coordinate. The
        // binding compares it with the requested target to expose lag or
        // clamping instead of silently claiming the target is displayed.
        var cursorSample = plan?.CursorSample ?? requestedCursor;
        var samplingRate = endSample / (plan?.EndSeconds ?? range?.EndSeconds ?? 1);
        var cursorSeconds = plan?.CursorSeconds ?? requestedCursor / samplingRate;
        var reason = response.Failure?.Code ?? response.Failure?.Message;
        var reasons = reason is null ? Array.Empty<string>() : new[] { reason };
        // Preserve the backend's scientific terminal state. Collapsing rejected,
        // insufficient, cancelled and failed into one unavailable value makes
        // the review layer unable to explain whether data or execution failed.
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
        var axes = result?.Axes is null
            ? null
            : result.Axes.ToDictionary(item => item.Key == "frequency_hz" ? "axis_frequency_hz" : item.Key, item => item.Value);
        var arrays = result?.Arrays;
        var metadata = result?.ArrayUnits?.ToDictionary(
            item => item.Key,
            item => JsonSerializer.SerializeToElement(new { unit = item.Value }))
            ?? new Dictionary<string, JsonElement>();
        return new WindowPreviewResponse(
            response.SessionId,
            recordingId,
            cursorSample,
            cursorSeconds,
            state,
            reasons,
            new WindowPreviewWindow(response.WindowId, 0, startSample, endSample,
                response.State, response.Quality ?? "unknown", reasons),
            false,
            null,
            null,
            axes,
            null,
            arrays,
            metadata,
            identity.TryGetProperty("scientific_version", out var science) ? science.GetString() : null,
            identity.TryGetProperty("implementation_identity", out var implementation) ? implementation.GetString() : null);
    }
}
