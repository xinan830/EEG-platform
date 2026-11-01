using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

internal interface IReviewTnkPlaybackEvents
{
    event Action<DynamicWindowResponse>? WindowCompleted;
    event Action<TnkPlaybackGap>? GapReported;
    bool IsCurrent(DynamicWindowResponse response);
    bool IsCurrent(TnkPlaybackGap gap);
}
