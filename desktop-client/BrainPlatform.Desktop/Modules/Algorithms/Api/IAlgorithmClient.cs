using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

public interface IAlgorithmClient
{
    Task<TnkWorkbenchSummary> GetTnkWorkbenchSummaryAsync(string runId, double cursorSeconds, CancellationToken token) =>
        throw new NotSupportedException("当前客户端不支持 TNK 状态统计。");
    Task<BrainPlatform.Desktop.Shared.Waveform.SignalPreviewResponse> GetSignalPreviewAsync(
        string recordingId, BrainPlatform.Desktop.Shared.Waveform.SignalPreviewRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前客户端不支持波形预览。");
    Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken);

    Task<AnalysisRunResponse> CreateRunAsync(
        AnalysisRunRequest request,
        CancellationToken cancellationToken);

    Task<AnalysisRunResponse> EnsureRunAsync(
        AnalysisRunRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前算法客户端不支持结果复用入口。");

    Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken);

    Task<AnalysisRunResponse> CancelRunAsync(string runId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前算法客户端不支持取消分析。");

    Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken);

    Task<StructuredPreviewResponse> GetStructuredPreviewAsync(
        string runId,
        int maxCells,
        CancellationToken cancellationToken);

    Task<WindowPreviewResponse> GetWindowPreviewAsync(
        string runId,
        long? cursorSample,
        double? cursorSeconds,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前算法客户端不支持结果窗口读取。");

    Task<DynamicWindowResponse> EnsureDynamicWindowAsync(
        DynamicWindowRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前算法客户端不支持按需动态窗口。");

    Task<DynamicWindowResponse> GetDynamicWindowAsync(
        string windowId,
        string? projectId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前算法客户端不支持按需动态窗口。");

    Task<DynamicSubscriptionResponse> SubscribeDynamicWindowAsync(
        string windowId,
        string requestId,
        string? projectId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前算法客户端不支持按需动态窗口订阅。");

    Task<DynamicSubscriptionResponse> CancelDynamicSubscriptionAsync(
        string subscriptionId,
        string? projectId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前算法客户端不支持按需动态窗口订阅。");
}
