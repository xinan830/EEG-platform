using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

public sealed class PsdReviewAnalysisAdapter : ReviewWindowAnalysisAdapter, IReviewTopomapProvider, IReviewScalarProvider
{
    private readonly IAlgorithmClient client;

    public PsdReviewAnalysisAdapter(IAlgorithmClient client, bool? enableOnDemand = null)
        : base(client, "psd", "PSD", enableOnDemand) => this.client = client;

    public Task<TopomapResponse> LoadTopomapAsync(WindowPreviewResponse window,
        CancellationToken cancellationToken, IReadOnlyList<string> fieldIds)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(fieldIds);
        if (window.DynamicResponse is not { } dynamic)
            throw new InvalidOperationException("PSD 地形图需要当前动态 PSD 窗口结果。");
        return client.GetDynamicTopomapAsync(dynamic.WindowId, null, cancellationToken, fieldIds);
    }

    public Task<ResultScalarsResponse> LoadScalarsAsync(WindowPreviewResponse window,
        CancellationToken cancellationToken, IReadOnlyList<string> fieldIds) =>
        client.GetDynamicScalarValuesAsync(window.Window?.WindowId ??
            throw new InvalidOperationException("PSD 标量读取需要当前动态窗口。"), null, cancellationToken, fieldIds);
}
