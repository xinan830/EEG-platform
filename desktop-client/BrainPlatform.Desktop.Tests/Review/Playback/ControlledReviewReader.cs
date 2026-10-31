using System.Threading.Channels;

namespace BrainPlatform.Desktop.Tests.Review;

/// <summary>Separates navigation timing from completion, deliberately ignoring cancellation.</summary>
internal sealed class ControlledReviewReader : IRecordingAccessSession, IDisposable
{
    private readonly Channel<PendingRead> pending = Channel.CreateUnbounded<PendingRead>();
    private readonly List<PendingRead> reads = [];

    public LocalRawRecordingManifest Manifest { get; } = new(
        Guid.NewGuid(), "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
        "V", "fixture", "fixture", 20, 2, DateTimeOffset.UnixEpoch,
        [new AcquisitionChannel(0, 3, "F3", AcquisitionChannelKind.Reference, "V"),
         new AcquisitionChannel(1, 99, "Counter", AcquisitionChannelKind.SampleCounter, "count")],
        new AcquisitionProjectContext("fixture", "fixture", "fixture", "C:\\fixture", "{}"),
        new Dictionary<string, string>());

    public double DurationSeconds => 300;
    public long FirstSampleCounter => 1000;
    public int DeferredReadCount { get { lock (reads) return reads.Count; } }
    public static RecordingMontageCatalogResult Catalog => new(null, AcquisitionMontageStatus.MissingSnapshot,
        new RawSignalViewDefinition(["F3"]), [], []);

    public Task<RecordingReviewWindow> ReadWindowAsync(double start, double duration, CancellationToken token)
    {
        var samples = (int)Math.Ceiling(duration * Manifest.SamplingRateHz);
        var values = new double[samples * 2];
        var first = FirstSampleCounter + (long)Math.Round(start * Manifest.SamplingRateHz);
        for (var index = 0; index < samples; index++)
        {
            values[index * 2] = (start + 1) * 1e-6;
            values[index * 2 + 1] = first + index;
        }
        var window = new RecordingReviewWindow(start, start, start + duration,
            [new RecordingReviewSegment(first, samples, 2, values)]);
        if (start == 0) return Task.FromResult(window);
        var read = new PendingRead(window, token);
        lock (reads) reads.Add(read);
        pending.Writer.TryWrite(read);
        return read.Completion.Task;
    }

    public Task<RecordingAccessReadResult> ReadSamplesAsync(
        RecordingSampleRange range,
        IReadOnlyList<int>? channelIndexes,
        CancellationToken cancellationToken) =>
        RecordingAccessTestHelpers.ReadAsync(
            Manifest, FirstSampleCounter, ReadWindowAsync, range, channelIndexes, cancellationToken);

    public async Task<PendingRead> NextReadAsync() =>
        await pending.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    public void Dispose()
    {
        lock (reads)
            foreach (var read in reads) read.Complete();
    }

    internal sealed class PendingRead(RecordingReviewWindow window, CancellationToken token)
    {
        public TaskCompletionSource<RecordingReviewWindow> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; } = token;
        public double StartSeconds => window.ActualStartSeconds;
        public void Complete() => Completion.TrySetResult(window);
        public void Fail() => Completion.TrySetException(new IOException("obsolete fixture read failed"));
    }
}
