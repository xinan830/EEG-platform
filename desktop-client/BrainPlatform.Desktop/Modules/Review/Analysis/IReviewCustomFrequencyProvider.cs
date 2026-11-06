using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

public interface IReviewCustomFrequencyProvider
{
    Task<CustomFrequencyBinsResponse> LoadBinsAsync(WindowPreviewResponse psd, WindowPreviewResponse rbp,
        CancellationToken cancellationToken);

    Task<CustomFrequencyRangeResponse> LoadRangeAsync(WindowPreviewResponse psd, WindowPreviewResponse rbp,
        double lowHz, double highHz, CancellationToken cancellationToken);
}
