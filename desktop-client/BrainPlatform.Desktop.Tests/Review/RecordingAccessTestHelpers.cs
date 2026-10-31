internal static class RecordingAccessTestHelpers
{
    public static async Task<RecordingAccessReadResult> ReadAsync(
        LocalRawRecordingManifest manifest,
        long firstSampleCounter,
        Func<double, double, CancellationToken, Task<RecordingReviewWindow>> readWindow,
        RecordingSampleRange range,
        IReadOnlyList<int>? channelIndexes,
        CancellationToken cancellationToken)
    {
        range.Validate();
        var startSeconds = (range.StartSample - firstSampleCounter) / (double)manifest.SamplingRateHz;
        var durationSeconds = range.Length / (double)manifest.SamplingRateHz;
        var window = await readWindow(startSeconds, durationSeconds, cancellationToken);
        var indexes = channelIndexes?.ToArray() ?? Enumerable.Range(0, manifest.Channels.Count).ToArray();
        var channels = indexes.Select(index => manifest.Channels[index]).ToArray();
        return new RecordingAccessReadResult(
            range,
            new RecordingSampleRange(
                window.Segments.FirstOrDefault()?.FirstSampleCounter ?? range.StartSample,
                (window.Segments.LastOrDefault()?.LastSampleCounter ?? (range.EndSampleExclusive - 1L)) + 1L),
            channels,
            window);
    }
}
