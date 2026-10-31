namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewSourceLoaderBudgetTests
{
    [Fact]
    public async Task Source_build_admission_allows_two_builds_and_waits_for_a_slot()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-review-budget", Guid.NewGuid().ToString("N"));
        var filter = new BlockingFilter();
        var reader = new Reader();
        var loader = new ReviewSourceLoader(reader, filter, new RecordingReviewWindowCache(),
            (_, _, _, _) => { }, new ReviewFilteredSourceChunkCache(root));
        try
        {
            var first = loader.LoadAsync(0, 1, Settings, CancellationToken.None);
            var second = loader.LoadAsync(10, 11, Settings, CancellationToken.None);
            await filter.TwoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var third = loader.LoadAsync(20, 21, Settings, CancellationToken.None);
            await Task.Delay(50);

            Assert.Equal(2, filter.StartedCount);
            Assert.False(third.IsCompleted);

            filter.Release.TrySetResult();
            await Task.WhenAll(first, second, third);
        }
        finally
        {
            filter.Release.TrySetResult();
            await loader.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static RecordingReviewFilterSettings Settings => new(1, 4, null);

    private sealed class Reader : IRecordingAccessSession
    {
        public LocalRawRecordingManifest Manifest { get; } = new(
            Guid.NewGuid(), "fixture", "V", "fixture", "fixture", 10, 1, DateTimeOffset.UnixEpoch,
            [new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V")],
            new AcquisitionProjectContext("p", "p", "p", "C:\\fixture", "{}"), new Dictionary<string, string>());

        public double DurationSeconds => 60;
        public Task<RecordingReviewWindow> ReadWindowAsync(double start, double duration, CancellationToken token)
        {
            var count = Math.Max(1, (int)Math.Ceiling(duration * Manifest.SamplingRateHz));
            return Task.FromResult(new RecordingReviewWindow(start, start, start + duration,
                [new RecordingReviewSegment((long)(start * 10), count, 1, new double[count])]));
        }

        public Task<RecordingAccessReadResult> ReadSamplesAsync(
            RecordingSampleRange range,
            IReadOnlyList<int>? channelIndexes,
            CancellationToken cancellationToken) =>
            RecordingAccessTestHelpers.ReadAsync(
                Manifest, 0, ReadWindowAsync, range, channelIndexes, cancellationToken);
    }

    private sealed class BlockingFilter : IRecordingReviewFilter
    {
        private int started;
        public TaskCompletionSource TwoStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StartedCount => Volatile.Read(ref started);

        public async Task<RecordingReviewWindow> ApplyWithWarmupAsync(
            RecordingReviewWindow? warmup, RecordingReviewWindow source, LocalRawRecordingManifest manifest,
            RecordingReviewFilterSettings settings, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref started) == ReviewSourceLoader.MaximumConcurrentSourceBuilds)
                TwoStarted.TrySetResult();
            await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return source;
        }

        public Task<RecordingReviewWindow> ApplyAsync(RecordingReviewWindow source,
            LocalRawRecordingManifest manifest, RecordingReviewFilterSettings settings,
            CancellationToken cancellationToken) => Task.FromResult(source);
    }
}
