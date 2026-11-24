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

        cache.StoreFrame(frame, new RecordingReviewFilterSettings(1, 30, 50), null, null, false);

        Assert.True(cache.TryGetFrame(0, 1, new RecordingReviewFilterSettings(1, 30, 50), null, null,
            out var retained, out _));
        Assert.Equal(frame.Segments, retained.Segments);
        Assert.Equal(frame.OutputChannelNames, retained.OutputChannelNames);
    }

    [Fact]
    public void Temporary_profile_cache_is_separate_and_clearing_frames_preserves_raw_windows()
    {
        var cache = new RecordingReviewWindowCache();
        var settings = new RecordingReviewFilterSettings(1, 30, 50);
        var temporary = TemporaryMontageFixture.Create("F3", "F4");
        var frame = new RecordingReviewFrame(Guid.NewGuid(), 1, 10, 0, 0, 1, "F3-F4",
            [new ProjectedMontageSegment(0, 1, [new ProjectedMontageChannel(temporary.Name, [1d])])],
            [temporary.Name], "fixture-source");
        cache.StoreRaw(Window(0, 1, 2));
        cache.StoreFrame(frame, settings, null, temporary, false);

        Assert.True(cache.TryGetFrame(0, 1, settings, null, temporary, out _, out _));
        Assert.False(cache.TryGetFrame(0, 1, settings, null, null, out _, out _));
        Assert.False(cache.TryGetFrame(0, 1, settings, null,
            TemporaryMontageFixture.Create("F4", "F3"), out _, out _));
        var recolored = temporary with
        {
            DerivedChannels = temporary.DerivedChannels.Select(channel => channel with { Color = "#654321" }).ToArray(),
        };
        Assert.False(cache.TryGetFrame(0, 1, settings, null, recolored, out _, out _));

        cache.ClearFrames();

        Assert.False(cache.TryGetFrame(0, 1, settings, null, temporary, out _, out _));
        Assert.True(cache.TryGetRaw(0, 1, out _));
    }

    private static RecordingReviewWindow Window(double start, int sampleCount, int channelCount) =>
        new(start, start, start + 1,
            [new RecordingReviewSegment(100, sampleCount, channelCount,
                Enumerable.Range(0, sampleCount * channelCount).Select(index => (double)index + 1).ToArray())]);
}
