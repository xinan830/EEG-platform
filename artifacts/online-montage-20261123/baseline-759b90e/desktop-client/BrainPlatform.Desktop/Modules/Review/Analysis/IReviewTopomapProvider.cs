using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

internal interface IReviewTopomapProvider
{
    Task<TopomapResponse> LoadTopomapAsync(WindowPreviewResponse window,
        CancellationToken cancellationToken, IReadOnlyList<string> fieldIds);
}
