namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RecordingReviewWindowCacheTests
{
    [Fact]
    public void Raw_payload_budget_evicts_old_entries_without_truncating_retained_windows()
    {
        var cache = new RecordingReviewWindowCache(maximumRawPayloadBytes: 16, maximumFramePayloadBytes: 16);
        var first = Window(0, 1, 2);
        var second = Window(1, 1, 2);

        cache.StoreRaw(first);
        cache.StoreRaw(second);

        Assert.False(cache.TryGetRaw(0, 1, out _));
        Assert.True(cache.TryGetRaw(1, 2, out var retained));
        Assert.Equal(2, retained.Segments[0].SampleMajorValues.Length);
    }

    [Fact]
    public void Oversized_raw_payload_is_not_cached_or_modified()
    {
        var cache = new RecordingReviewWindowCache(maximumRawPayloadBytes: 8, maximumFramePayloadBytes: 8);
        var window = Window(0, 1, 2);

        cache.StoreRaw(window);

        Assert.False(cache.TryGetRaw(0, 1, out _));
        Assert.Equal(new[] { 1d, 2d }, window.Segments[0].SampleMajorValues);
    }

    [Fact]
    public void Frame_payload_budget_is_independent_from_raw_payload_budget()
    {
        var cache = new RecordingReviewWindowCache(maximumRawPayloadBytes: 1024, maximumFramePayloadBytes: 16);
        var frame = new RecordingReviewFrame(Guid.NewGuid(), 1, 10, 0, 0, 1, "F3",
            [new ProjectedMontageSegment(0, 2, [new ProjectedMontageChannel("F3", [1d, 2d])])], ["F3"], "fixture-source");

        cache.StoreFrame(frame, new RecordingReviewFilterSettings(1, 30, 50), null, false);

        Assert.True(cache.TryGetFrame(0, 1, new RecordingReviewFilterSettings(1, 30, 50), null,
            out var retained, out _));
        Assert.Equal(frame.Segments, retained.Segments);
        Assert.Equal(frame.OutputChannelNames, retained.OutputChannelNames);
    }

    private static RecordingReviewWindow Window(double start, int sampleCount, int channelCount) =>
        new(start, start, start + 1,
            [new RecordingReviewSegment(100, sampleCount, channelCount,
                Enumerable.Range(0, sampleCount * channelCount).Select(index => (double)index + 1).ToArray())]);
}
