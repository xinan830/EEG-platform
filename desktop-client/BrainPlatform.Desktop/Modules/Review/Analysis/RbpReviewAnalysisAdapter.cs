using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

public sealed class RbpReviewAnalysisAdapter : ReviewWindowAnalysisAdapter, IReviewTopomapProvider, IReviewScalarProvider
{
    private readonly IAlgorithmClient client;

    public RbpReviewAnalysisAdapter(IAlgorithmClient client, bool? enableOnDemand = null)
        : base(client, "rbp", "RBP", enableOnDemand) => this.client = client;

    public Task<TopomapResponse> LoadTopomapAsync(WindowPreviewResponse window,
        CancellationToken cancellationToken, IReadOnlyList<string> fieldIds)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(fieldIds);
        if (window.DynamicResponse is { } dynamic)
            return client.GetDynamicTopomapAsync(dynamic.WindowId, null, cancellationToken, fieldIds);
        return client.GetRunTopomapAsync(window.RunId, window.CursorSample, cancellationToken, fieldIds);
    }

    public Task<ResultScalarsResponse> LoadScalarsAsync(WindowPreviewResponse window,
        CancellationToken cancellationToken, IReadOnlyList<string> fieldIds) =>
        client.GetDynamicScalarValuesAsync(window.Window?.WindowId ??
            throw new InvalidOperationException("RBP 标量读取需要当前动态窗口。"), null, cancellationToken, fieldIds);
}
