namespace BrainPlatform.Desktop.Tests.Review;

public enum CheckpointPauseAt { Open, Advance, Filter, Export }

internal sealed class ControlledCheckpointFixture : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "brain-checkpoint-lifetime", Guid.NewGuid().ToString("N"));
    public Reader Source { get; } = new();
    public Filter Backend { get; }
    public ReviewCheckpointCoordinator Coordinator { get; }
    public static RecordingReviewFilterSettings Settings => new(1, 4, null);
    public static ReviewFilterContract Contract => new("fixture", "fixture", "checkpoint-v1");

    public ControlledCheckpointFixture(CheckpointPauseAt stage)
    {
        Backend = new Filter(stage);
        Coordinator = new(Source, Backend, new ReviewCheckpointAnchorCache(root));
    }

    public Task<RecordingReviewFilterChunkResult?> Start(CancellationToken token = default) =>
        Coordinator.FilterAsync(Source.Window(10, 1), Settings, Contract, token);

    public void Dispose()
    {
        Backend.Release.TrySetResult();
        Coordinator.DisposeAsync().GetAwaiter().GetResult();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    internal sealed class Reader(int samplingRateHz = 10) : IRecordingAccessSession
    {
        public LocalRawRecordingManifest Manifest { get; } = new(
            Guid.NewGuid(), "fixture", "V", "fixture", "fixture", samplingRateHz, 1, DateTimeOffset.UnixEpoch,
            [new AcquisitionChannel(0, 3, "F3", AcquisitionChannelKind.Reference, "V")],
            new AcquisitionProjectContext("p", "p", "p", "C:\\fixture", "{}"), new Dictionary<string, string>());
        public long FirstSampleCounter => 1000;
        public double DurationSeconds => 300;
        public long GetContiguousSegmentOrigin(long counter) => FirstSampleCounter;
        public Task<RecordingReviewWindow> ReadWindowAsync(double start, double duration, CancellationToken token) =>
            Task.FromResult(Window(start, duration));

        public Task<RecordingAccessReadResult> ReadSamplesAsync(
            RecordingSampleRange range,
            IReadOnlyList<int>? channelIndexes,
            CancellationToken cancellationToken) =>
            RecordingAccessTestHelpers.ReadAsync(
                Manifest, FirstSampleCounter, ReadWindowAsync, range, channelIndexes, cancellationToken);
        public RecordingReviewWindow Window(double start, double duration)
        {
            var count = (int)Math.Round(duration * Manifest.SamplingRateHz);
            return new(start, start, start + duration,
                [new(FirstSampleCounter + (long)Math.Round(start * Manifest.SamplingRateHz), count, 1,
                    Enumerable.Repeat(2e-6, count).ToArray())]);
        }
    }

    internal sealed class Filter(CheckpointPauseAt stage) : IRecordingReviewFilter
    {
        private int paused;
        private int disposedSessions;
        private int openedSessions;
        public TaskCompletionSource<CancellationToken> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposedSessions => Volatile.Read(ref disposedSessions);
        public int OpenedSessions => Volatile.Read(ref openedSessions);

        private async Task PauseAsync(CheckpointPauseAt current, CancellationToken token)
        {
            if (current != stage || Interlocked.Exchange(ref paused, 1) != 0) return;
            Entered.TrySetResult(token);
            await Release.Task; // Deliberately ignores cancellation to expose late completion.
        }

        public Task<RecordingReviewWindow> ApplyAsync(RecordingReviewWindow source,
            LocalRawRecordingManifest manifest, RecordingReviewFilterSettings settings, CancellationToken token) =>
            Task.FromResult(source);

        public async Task<IRecordingReviewCheckpointSession?> OpenCheckpointSessionAsync(
            LocalRawRecordingManifest manifest, RecordingReviewFilterSettings settings, CancellationToken token)
        {
            await PauseAsync(CheckpointPauseAt.Open, token);
            Interlocked.Increment(ref openedSessions);
            return new Session(this);
        }

        private sealed class Session(Filter owner) : IRecordingReviewCheckpointSession
        {
            private long next;
            public Task RestoreAsync(string checkpoint, CancellationToken token)
            {
                next = long.Parse(checkpoint, System.Globalization.CultureInfo.InvariantCulture);
                return Task.CompletedTask;
            }
            public Task AdvanceAsync(RecordingReviewSegment source, CancellationToken token) =>
                AdvanceAndExportAsync(source, token);
            public async Task<string> AdvanceAndExportAsync(RecordingReviewSegment source, CancellationToken token)
            {
                await owner.PauseAsync(CheckpointPauseAt.Advance, token);
                next = source.LastSampleCounter + 1;
                return next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            public async Task<RecordingReviewSegment> FilterAsync(RecordingReviewSegment source, CancellationToken token)
            {
                await owner.PauseAsync(CheckpointPauseAt.Filter, token);
                next = source.LastSampleCounter + 1;
                return source;
            }
            public async Task<string> ExportAsync(CancellationToken token)
            {
                await owner.PauseAsync(CheckpointPauseAt.Export, token);
                return next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.disposedSessions);
                return ValueTask.CompletedTask;
            }
        }
    }
}
