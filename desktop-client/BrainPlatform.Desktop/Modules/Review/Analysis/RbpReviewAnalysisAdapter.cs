using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

public sealed class RbpReviewAnalysisAdapter : ReviewWindowAnalysisAdapter, IReviewTopomapProvider
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
}
