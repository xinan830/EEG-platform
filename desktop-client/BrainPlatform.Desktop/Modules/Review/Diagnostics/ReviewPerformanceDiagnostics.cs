using BrainPlatform.Desktop.Infrastructure.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Review.Diagnostics;

public enum ReviewPerformanceStage
{
    RawRead,
    FilteredSource,
    FrameBuild,
    ChartBinding,
}

public enum ReviewPerformanceSource
{
    MemoryCache,
    Disk,
    FilterCache,
    FilterService,
    RawFallback,
    PublishedFrame,
}

public sealed record ReviewPerformanceSample(
    Guid RecordingSessionId,
    ReviewPerformanceStage Stage,
    ReviewPerformanceSource Source,
    long? Revision,
    double StartSeconds,
    double EndSeconds,
    int ChannelCount,
    int PointCount,
    double ElapsedMilliseconds);

public interface IReviewPerformanceSink
{
    void Record(ReviewPerformanceSample sample);
}

/// <summary>
/// Opt-in review telemetry. It records timing and bounded shape metadata only;
/// it never receives raw EEG values or request payloads.
/// </summary>
public static class ReviewPerformanceDiagnostics
{
    private static readonly bool enabled = string.Equals(
        Environment.GetEnvironmentVariable("BRAIN_REVIEW_PERF_DIAGNOSTICS"),
        "1",
        StringComparison.Ordinal);

    public static bool Enabled => enabled;

    public static IReviewPerformanceSink Default { get; } = new DesktopSink();

    private sealed class DesktopSink : IReviewPerformanceSink
    {
        public void Record(ReviewPerformanceSample sample)
        {
            if (!Enabled) return;

            DesktopLog.Info(
                $"review_perf recording_id={sample.RecordingSessionId:N}" +
                $" stage={sample.Stage} source={sample.Source}" +
                $" revision={sample.Revision?.ToString() ?? "none"}" +
                $" start_s={sample.StartSeconds:0.######}" +
                $" end_s={sample.EndSeconds:0.######}" +
                $" channels={sample.ChannelCount}" +
                $" points={sample.PointCount}" +
                $" elapsed_ms={sample.ElapsedMilliseconds:0.0}");
        }
    }
}
