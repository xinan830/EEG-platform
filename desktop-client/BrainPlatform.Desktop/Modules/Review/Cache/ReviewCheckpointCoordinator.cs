namespace BrainPlatform.Desktop.Modules.Review.Cache;

public enum ReviewPreparationPhase
{
    Idle,
    PreparingHistory,
    FilteringTarget,
    Complete,
    Failed,
}

public sealed record ReviewPreparationState(
    ReviewPreparationPhase Phase,
    long SegmentOriginSampleCounter,
    long ProcessedNextSampleCounter,
    long RequiredTargetSampleCounter,
    string? Failure = null)
{
    public double? Progress => RequiredTargetSampleCounter <= SegmentOriginSampleCounter
        ? null
        : Math.Clamp((ProcessedNextSampleCounter - SegmentOriginSampleCounter) /
                     (double)(RequiredTargetSampleCounter - SegmentOriginSampleCounter), 0, 1);

    public static readonly ReviewPreparationState Idle = new(ReviewPreparationPhase.Idle, 0, 0, 0);
}

internal interface IRecordingReviewTimeline
{
    long FirstSampleCounter { get; }
    long GetContiguousSegmentOrigin(long sampleCounter);
}

/// <summary>Serializes exact Python checkpoint work for one review session.</summary>
internal sealed class ReviewCheckpointCoordinator(
    IRecordingReviewReader reader,
    IRecordingReviewFilter filter,
    ReviewCheckpointAnchorCache anchorCache) : IAsyncDisposable
{
    // Cold seeks in long recordings must preserve exact causal history, but a
    // 10-second HTTP round trip is needlessly expensive. Keep each request
    // bounded by both time and payload size so low-rate/high-channel records
    // do not create oversized allocations.
    private const double MaximumAdvanceBatchSeconds = 1800;
    private const long MaximumAdvancePayloadBytes = 32L * 1024 * 1024;
    private readonly SemaphoreSlim workGate = new(1, 1);
    private readonly object lifetimeGate = new();
    private readonly CancellationTokenSource lifetime = new();
    private int operations;
    private bool cancellationSent;
    private int disposed;

    public event EventHandler<ReviewPreparationState>? ProgressChanged;

    public async Task<RecordingReviewFilterChunkResult?> FilterAsync(
        RecordingReviewWindow source,
        RecordingReviewFilterSettings settings,
        ReviewFilterContract contract,
        CancellationToken cancellationToken,
        Action<ReviewPreparationState>? progress = null)
    {
        var operation = BeginOperation(cancellationToken);
        var token = operation.Token;
        var acquired = false;
        try
        {
            await workGate.WaitAsync(token);
            acquired = true;
            token.ThrowIfCancellationRequested();
            var output = new List<RecordingReviewSegment>(source.Segments.Count);
            string? lastCheckpoint = null;
            foreach (var target in source.Segments)
            {
                var result = await FilterSegmentAsync(target, settings, contract, token, progress);
                if (result is null) return null;
                output.Add(result.Value.Segment);
                lastCheckpoint = result.Value.Checkpoint;
            }
            token.ThrowIfCancellationRequested();
            Publish(new ReviewPreparationState(ReviewPreparationPhase.Complete, 0, 0, 0), token, progress);
            return new RecordingReviewFilterChunkResult(source with { Segments = output }, lastCheckpoint);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(new ReviewPreparationState(ReviewPreparationPhase.Failed, 0, 0, 0, exception.Message), token, progress);
            throw;
        }
        finally
        {
            if (acquired) workGate.Release();
            operation.Dispose();
            EndOperation();
        }
    }

    private async Task<(RecordingReviewSegment Segment, string Checkpoint)?> FilterSegmentAsync(
        RecordingReviewSegment target,
        RecordingReviewFilterSettings settings,
        ReviewFilterContract contract,
        CancellationToken cancellationToken,
        Action<ReviewPreparationState>? progress)
    {
        var group = ReviewCheckpointAnchorGroupKey.Create(reader.Manifest, settings, contract);
        var firstCounter = await GetFirstSampleCounterAsync(cancellationToken);
        var segmentOrigin = reader is IRecordingReviewTimeline timeline
            ? timeline.GetContiguousSegmentOrigin(target.FirstSampleCounter)
            : firstCounter;
        var anchor = await anchorCache.FindNearestAsync(
            group, segmentOrigin, target.FirstSampleCounter, cancellationToken);
        var cursor = anchor?.NextSampleCounter ?? segmentOrigin;
        var session = await filter.OpenCheckpointSessionAsync(reader.Manifest, settings, cancellationToken);
        if (session is null) return null;
        await using var ownedSession = new CheckpointSessionOwner(session);
        cancellationToken.ThrowIfCancellationRequested();
        if (anchor is not null)
            await ownedSession.Current.RestoreAsync(anchor.CheckpointB64, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        Publish(new ReviewPreparationState(ReviewPreparationPhase.PreparingHistory,
            segmentOrigin, cursor, target.FirstSampleCounter), cancellationToken, progress);
        while (cursor < target.FirstSampleCounter)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = target.FirstSampleCounter - cursor;
            var count = Math.Min(remaining, GetAdvanceBatchSampleCount(
                reader.Manifest.SamplingRateHz, target.ChannelCount));
            var startSeconds = (cursor - firstCounter) / (double)reader.Manifest.SamplingRateHz;
            var durationSeconds = count / (double)reader.Manifest.SamplingRateHz;
            var history = await reader.ReadWindowAsync(startSeconds, durationSeconds, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (history.Segments.Count == 0)
                throw new InvalidOperationException("精确滤波历史中没有可读取的原始样本。");

            var advanced = false;
            foreach (var rawSegment in history.Segments)
            {
                if (rawSegment.LastSampleCounter < cursor) continue;
                if (rawSegment.FirstSampleCounter > cursor)
                {
                    var replacement = await filter.OpenCheckpointSessionAsync(reader.Manifest, settings, cancellationToken)
                        ?? throw new InvalidOperationException("滤波器不支持 checkpoint 会话。");
                    await ownedSession.ReplaceAsync(replacement);
                    segmentOrigin = rawSegment.FirstSampleCounter;
                    cursor = segmentOrigin;
                }

                var bounded = Slice(rawSegment, cursor, target.FirstSampleCounter);
                if (bounded is null) continue;
                var checkpoint = await ownedSession.Current.AdvanceAndExportAsync(bounded, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                cursor = bounded.LastSampleCounter + 1L;
                await anchorCache.StoreAsync(group, segmentOrigin, cursor, checkpoint, cancellationToken);
                Publish(new ReviewPreparationState(ReviewPreparationPhase.PreparingHistory,
                    segmentOrigin, cursor, target.FirstSampleCounter), cancellationToken, progress);
                advanced = true;
                if (cursor >= target.FirstSampleCounter) break;
            }
            if (!advanced)
                throw new InvalidOperationException("精确滤波历史没有向目标样本推进。");
        }

        if (cursor != target.FirstSampleCounter)
            throw new InvalidOperationException("checkpoint 与目标样本计数器不连续。");
        Publish(new ReviewPreparationState(ReviewPreparationPhase.FilteringTarget,
            segmentOrigin, cursor, target.LastSampleCounter + 1L), cancellationToken, progress);
        var filtered = await ownedSession.Current.FilterAsync(target, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        cursor = target.LastSampleCounter + 1L;
        var finalCheckpoint = await ownedSession.Current.ExportAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await anchorCache.StoreAsync(group, segmentOrigin, cursor, finalCheckpoint, cancellationToken);
        return (filtered, finalCheckpoint);
    }

    private async Task<long> GetFirstSampleCounterAsync(CancellationToken cancellationToken)
    {
        if (reader is IRecordingReviewTimeline timeline) return timeline.FirstSampleCounter;
        var duration = Math.Min(reader.DurationSeconds, 1d / reader.Manifest.SamplingRateHz);
        var first = await reader.ReadWindowAsync(0, duration, cancellationToken);
        return first.Segments.FirstOrDefault()?.FirstSampleCounter
            ?? throw new InvalidOperationException("记录开头没有可读取的样本。");
    }

    private static long GetAdvanceBatchSampleCount(int samplingRateHz, int channelCount)
    {
        if (samplingRateHz <= 0 || channelCount <= 0)
            throw new ArgumentOutOfRangeException();

        var byTime = checked((long)Math.Ceiling(MaximumAdvanceBatchSeconds * samplingRateHz));
        var bytesPerSample = checked((long)channelCount * sizeof(double));
        var byPayload = Math.Max(1L, MaximumAdvancePayloadBytes / bytesPerSample);
        return Math.Max(1L, Math.Min(byTime, byPayload));
    }

    private static RecordingReviewSegment? Slice(
        RecordingReviewSegment source,
        long firstCounter,
        long endExclusive)
    {
        var start = Math.Max(source.FirstSampleCounter, firstCounter);
        var end = Math.Min(source.LastSampleCounter + 1L, endExclusive);
        if (end <= start) return null;
        var offset = checked((int)(start - source.FirstSampleCounter));
        var sampleCount = checked((int)(end - start));
        var values = new double[checked(sampleCount * source.ChannelCount)];
        Array.Copy(source.SampleMajorValues, checked(offset * source.ChannelCount), values, 0, values.Length);
        return new RecordingReviewSegment(start, sampleCount, source.ChannelCount, values);
    }

    private void Publish(ReviewPreparationState state, CancellationToken token, Action<ReviewPreparationState>? progress)
    {
        if (token.IsCancellationRequested || Volatile.Read(ref disposed) != 0) return;
        progress?.Invoke(state);
        if (!token.IsCancellationRequested && Volatile.Read(ref disposed) == 0)
            ProgressChanged?.Invoke(this, state);
    }

    private CancellationTokenSource BeginOperation(CancellationToken token)
    {
        lock (lifetimeGate)
        {
            ThrowIfDisposed();
            var operation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            operations++;
            return operation;
        }
    }

    private void EndOperation()
    {
        lock (lifetimeGate)
        {
            operations--;
            DisposeResourcesWhenIdle();
        }
    }

    private void DisposeResourcesWhenIdle()
    {
        if (operations != 0 || !cancellationSent) return;
        workGate.Dispose();
        lifetime.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        lock (lifetimeGate)
        {
            if (disposed != 0) return ValueTask.CompletedTask;
            Volatile.Write(ref disposed, 1);
        }
        // Do not wait for non-cooperative remote work, or dispose its gate early.
        try { lifetime.Cancel(); }
        finally
        {
            lock (lifetimeGate)
            {
                cancellationSent = true;
                DisposeResourcesWhenIdle();
            }
        }
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref disposed) != 0)
            throw new ObjectDisposedException(nameof(ReviewCheckpointCoordinator));
    }

    private sealed class CheckpointSessionOwner(IRecordingReviewCheckpointSession current) : IAsyncDisposable
    {
        public IRecordingReviewCheckpointSession Current { get; private set; } = current;

        public async Task ReplaceAsync(IRecordingReviewCheckpointSession replacement)
        {
            await Current.DisposeAsync();
            Current = replacement;
        }

        public ValueTask DisposeAsync() => Current.DisposeAsync();
    }
}
