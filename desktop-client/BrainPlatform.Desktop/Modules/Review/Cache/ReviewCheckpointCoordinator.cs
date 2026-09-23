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
    private const double AdvanceBatchSeconds = 10;
    private readonly SemaphoreSlim workGate = new(1, 1);
    private int disposed;

    public event EventHandler<ReviewPreparationState>? ProgressChanged;

    public async Task<RecordingReviewFilterChunkResult?> FilterAsync(
        RecordingReviewWindow source,
        RecordingReviewFilterSettings settings,
        ReviewFilterContract contract,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await workGate.WaitAsync(cancellationToken);
        try
        {
            var output = new List<RecordingReviewSegment>(source.Segments.Count);
            string? lastCheckpoint = null;
            foreach (var target in source.Segments)
            {
                var result = await FilterSegmentAsync(target, settings, contract, cancellationToken);
                if (result is null) return null;
                output.Add(result.Value.Segment);
                lastCheckpoint = result.Value.Checkpoint;
            }
            Publish(new ReviewPreparationState(ReviewPreparationPhase.Complete, 0, 0, 0));
            return new RecordingReviewFilterChunkResult(source with { Segments = output }, lastCheckpoint);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(new ReviewPreparationState(ReviewPreparationPhase.Failed, 0, 0, 0, exception.Message));
            throw;
        }
        finally
        {
            workGate.Release();
        }
    }

    private async Task<(RecordingReviewSegment Segment, string Checkpoint)?> FilterSegmentAsync(
        RecordingReviewSegment target,
        RecordingReviewFilterSettings settings,
        ReviewFilterContract contract,
        CancellationToken cancellationToken)
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
        if (anchor is not null)
            await ownedSession.Current.RestoreAsync(anchor.CheckpointB64, cancellationToken);

        Publish(new ReviewPreparationState(ReviewPreparationPhase.PreparingHistory,
            segmentOrigin, cursor, target.FirstSampleCounter));
        while (cursor < target.FirstSampleCounter)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = target.FirstSampleCounter - cursor;
            var count = Math.Min(remaining,
                checked((long)Math.Ceiling(AdvanceBatchSeconds * reader.Manifest.SamplingRateHz)));
            var startSeconds = (cursor - firstCounter) / (double)reader.Manifest.SamplingRateHz;
            var durationSeconds = count / (double)reader.Manifest.SamplingRateHz;
            var history = await reader.ReadWindowAsync(startSeconds, durationSeconds, cancellationToken);
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
                await ownedSession.Current.AdvanceAsync(bounded, cancellationToken);
                cursor = bounded.LastSampleCounter + 1L;
                var checkpoint = await ownedSession.Current.ExportAsync(cancellationToken);
                await anchorCache.StoreAsync(group, segmentOrigin, cursor, checkpoint, cancellationToken);
                Publish(new ReviewPreparationState(ReviewPreparationPhase.PreparingHistory,
                    segmentOrigin, cursor, target.FirstSampleCounter));
                advanced = true;
                if (cursor >= target.FirstSampleCounter) break;
            }
            if (!advanced)
                throw new InvalidOperationException("精确滤波历史没有向目标样本推进。");
        }

        if (cursor != target.FirstSampleCounter)
            throw new InvalidOperationException("checkpoint 与目标样本计数器不连续。");
        Publish(new ReviewPreparationState(ReviewPreparationPhase.FilteringTarget,
            segmentOrigin, cursor, target.LastSampleCounter + 1L));
        var filtered = await ownedSession.Current.FilterAsync(target, cancellationToken);
        cursor = target.LastSampleCounter + 1L;
        var finalCheckpoint = await ownedSession.Current.ExportAsync(cancellationToken);
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

    private void Publish(ReviewPreparationState state) => ProgressChanged?.Invoke(this, state);

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref disposed, 1);
        workGate.Dispose();
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
