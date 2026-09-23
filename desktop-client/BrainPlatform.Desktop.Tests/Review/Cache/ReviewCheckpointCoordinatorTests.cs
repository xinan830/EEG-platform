using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewCheckpointCoordinatorTests
{
    [Fact]
    public async Task ColdDistantTargetStreamsBoundedHistoryAndWarmTargetResumesNearestAnchor()
    {
        var root = TemporaryDirectory();
        try
        {
            var reader = new StreamingReader(durationSeconds: 200, samplingRateHz: 10);
            var filter = new CountingCheckpointFilter();
            var cache = new ReviewCheckpointAnchorCache(root);
            await using (var coordinator = new ReviewCheckpointCoordinator(reader, filter, cache))
            {
                await coordinator.FilterAsync(reader.TargetWindow(100, 10), Settings(), Contract(), CancellationToken.None);
            }
            var coldAdvanced = filter.AdvancedSamples;
            var coldAnchorWrites = Directory.GetFiles(root, "*.bpra", SearchOption.AllDirectories).Length;
            Assert.Equal(1000, coldAdvanced);
            Assert.True(reader.MaximumRequestedSamples <= 100);
            Assert.InRange(coldAnchorWrites, 1, 11);
            Assert.Equal(1, filter.SessionCount);

            var warmFilter = new CountingCheckpointFilter();
            await using (var coordinator = new ReviewCheckpointCoordinator(reader, warmFilter, cache))
            {
                await coordinator.FilterAsync(reader.TargetWindow(110, 10), Settings(), Contract(), CancellationToken.None);
            }

            Assert.Equal(0, warmFilter.AdvancedSamples);
            Assert.Equal(100, warmFilter.FilteredSamples);
            Assert.Equal(1, warmFilter.SessionCount);
            Assert.InRange(Directory.GetFiles(root, "*.bpra", SearchOption.AllDirectories).Length,
                coldAnchorWrites, coldAnchorWrites + 1);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task GapStartsFreshCausalChainAtPostGapOrigin()
    {
        var root = TemporaryDirectory();
        try
        {
            var reader = new StreamingReader(200, 10, gapStart: 500, gapLength: 100);
            var filter = new CountingCheckpointFilter();
            await using var coordinator = new ReviewCheckpointCoordinator(
                reader, filter, new ReviewCheckpointAnchorCache(root));

            var result = await coordinator.FilterAsync(
                reader.TargetWindow(70, 10), Settings(), Contract(), CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(100, filter.AdvancedSamples);
            Assert.Equal(100, filter.FilteredSamples);
            Assert.Equal(600, filter.SessionStartStates.Single());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ProgressUsesSampleCountersAndFailureIsExplicit()
    {
        var root = TemporaryDirectory();
        try
        {
            var reader = new StreamingReader(20, 10);
            var filter = new CountingCheckpointFilter { FailOnAdvance = true };
            await using var coordinator = new ReviewCheckpointCoordinator(
                reader, filter, new ReviewCheckpointAnchorCache(root));
            var states = new List<ReviewPreparationState>();
            coordinator.ProgressChanged += (_, state) => states.Add(state);

            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.FilterAsync(
                reader.TargetWindow(10, 1), Settings(), Contract(), CancellationToken.None));

            Assert.Contains(states, state => state.Phase == ReviewPreparationPhase.PreparingHistory &&
                                            state.RequiredTargetSampleCounter == 100);
            Assert.Equal(ReviewPreparationPhase.Failed, states[^1].Phase);
            Assert.NotNull(states[^1].Failure);
        }
        finally { Directory.Delete(root, true); }
    }

    private static RecordingReviewFilterSettings Settings() => new(0.01, 4, null);
    private static ReviewFilterContract Contract() => new("test", "test-contract", "checkpoint-v1");

    private sealed class CountingCheckpointFilter : IRecordingReviewFilter
    {
        public int AdvancedSamples { get; private set; }
        public int FilteredSamples { get; private set; }
        public bool FailOnAdvance { get; init; }
        public List<long> SessionStartStates { get; } = [];
        public int SessionCount { get; private set; }

        public Task<RecordingReviewWindow> ApplyAsync(RecordingReviewWindow source,
            LocalRawRecordingManifest manifest, RecordingReviewFilterSettings settings,
            CancellationToken cancellationToken) => Task.FromResult(source);

        public Task<IRecordingReviewCheckpointSession?> OpenCheckpointSessionAsync(
            LocalRawRecordingManifest manifest, RecordingReviewFilterSettings settings,
            CancellationToken cancellationToken)
        {
            SessionCount++;
            return Task.FromResult<IRecordingReviewCheckpointSession?>(new Session(this));
        }

        private sealed class Session(CountingCheckpointFilter owner) : IRecordingReviewCheckpointSession
        {
            private long next;
            private bool hasState;

            public Task RestoreAsync(string checkpoint, CancellationToken cancellationToken)
            {
                next = long.Parse(checkpoint, System.Globalization.CultureInfo.InvariantCulture);
                hasState = true;
                owner.SessionStartStates.Add(next);
                return Task.CompletedTask;
            }

            public Task AdvanceAsync(RecordingReviewSegment source, CancellationToken cancellationToken)
            {
                if (owner.FailOnAdvance) throw new InvalidOperationException("planned failure");
                if (!hasState)
                {
                    next = source.FirstSampleCounter;
                    hasState = true;
                    owner.SessionStartStates.Add(next);
                }
                Assert.Equal(next, source.FirstSampleCounter);
                next = source.LastSampleCounter + 1L;
                owner.AdvancedSamples += source.SampleCount;
                return Task.CompletedTask;
            }

            public Task<RecordingReviewSegment> FilterAsync(
                RecordingReviewSegment source, CancellationToken cancellationToken)
            {
                if (!hasState)
                {
                    next = source.FirstSampleCounter;
                    hasState = true;
                    owner.SessionStartStates.Add(next);
                }
                Assert.Equal(next, source.FirstSampleCounter);
                next = source.LastSampleCounter + 1L;
                owner.FilteredSamples += source.SampleCount;
                return Task.FromResult(source with { SampleMajorValues = source.SampleMajorValues.ToArray() });
            }

            public Task<string> ExportAsync(CancellationToken cancellationToken) =>
                Task.FromResult(next.ToString(System.Globalization.CultureInfo.InvariantCulture));
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class StreamingReader : IRecordingReviewReader, IRecordingReviewTimeline
    {
        private readonly int gapStart;
        private readonly int gapLength;
        public StreamingReader(double durationSeconds, int samplingRateHz, int gapStart = -1, int gapLength = 0)
        {
            DurationSeconds = durationSeconds;
            this.gapStart = gapStart;
            this.gapLength = gapLength;
            Manifest = ManifestFor(samplingRateHz);
        }

        public LocalRawRecordingManifest Manifest { get; }
        public double DurationSeconds { get; }
        public long FirstSampleCounter => 0;
        public int MaximumRequestedSamples { get; private set; }

        public long GetContiguousSegmentOrigin(long sampleCounter) =>
            gapStart >= 0 && sampleCounter >= gapStart + gapLength ? gapStart + gapLength : 0;

        public Task<RecordingReviewWindow> ReadWindowAsync(
            double startSeconds, double durationSeconds, CancellationToken cancellationToken)
        {
            var start = (long)Math.Floor(startSeconds * Manifest.SamplingRateHz);
            var count = (int)Math.Ceiling(durationSeconds * Manifest.SamplingRateHz);
            MaximumRequestedSamples = Math.Max(MaximumRequestedSamples, count);
            return Task.FromResult(CreateWindow(startSeconds, start, count));
        }

        public RecordingReviewWindow TargetWindow(double startSeconds, double durationSeconds)
        {
            var start = (long)(startSeconds * Manifest.SamplingRateHz);
            var count = (int)(durationSeconds * Manifest.SamplingRateHz);
            return CreateWindow(startSeconds, start, count);
        }

        private RecordingReviewWindow CreateWindow(double requestedStart, long start, int count)
        {
            var end = start + count;
            var segments = new List<RecordingReviewSegment>();
            AddSegment(segments, start, Math.Min(end, gapStart < 0 ? end : gapStart));
            if (gapStart >= 0) AddSegment(segments, Math.Max(start, gapStart + gapLength), end);
            return new RecordingReviewWindow(requestedStart, requestedStart,
                requestedStart + count / (double)Manifest.SamplingRateHz, segments);
        }

        private static void AddSegment(List<RecordingReviewSegment> segments, long start, long end)
        {
            if (end <= start) return;
            var count = checked((int)(end - start));
            var values = new double[count * 3];
            for (var index = 0; index < count; index++) values[index * 3] = (start + index) * 1e-9;
            segments.Add(new RecordingReviewSegment(start, count, 3, values));
        }

        private static LocalRawRecordingManifest ManifestFor(int rate) => new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "float64", "V", "device", "device",
            rate, 2, DateTimeOffset.UnixEpoch,
            [
                new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(1, 1, "F4", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(2, 2, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
            ],
            new AcquisitionProjectContext("p", "P", "P", "C:\\p", "{}"),
            new Dictionary<string, string>());
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "brain-platform-coordinator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
