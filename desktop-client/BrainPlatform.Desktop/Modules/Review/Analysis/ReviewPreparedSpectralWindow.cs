using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

internal sealed record ReviewPreparedSpectralWindow(WindowPreviewResponse Psd, WindowPreviewResponse Rbp)
{
    public CustomFrequencyBinsResponse? Bins { get; init; }
    public CustomFrequencyRangeResponse? Range { get; init; }
    public double? RangeLowHz { get; init; }
    public double? RangeHighHz { get; init; }
    public ResultScalarsResponse? Density { get; init; }
    public ResultScalarsResponse? Amplitude { get; init; }
    public Exception? ProjectionError { get; init; }
}
