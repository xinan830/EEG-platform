using System.Diagnostics;
using System.Net.Http;
using BrainPlatform.Desktop.Modules.Review.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Review.Cache;

internal sealed record ReviewSourceLoadResult(
    RecordingReviewWindow Window,
    bool FilterUnavailable,
    ReviewPerformanceSource Source,
    ReviewFilterContract? FilterContract = null);

/// <summary>Owns disposable raw/filtered source loading, shared chunk work and checkpoint lifetime.</summary>
internal sealed class ReviewSourceLoader : IAsyncDisposable
{
    private const double FilteredSourceChunkSeconds = 10;
    private const double MinimumFilterWarmupSeconds = 5;
    private const double MaximumFilterWarmupSeconds = 30;
    private readonly IRecordingReviewReader reader;
    private readonly IRecordingReviewFilter? filter;
    private readonly RecordingReviewWindowCache cache;
    private readonly ReviewFilteredSourceChunkCache filteredSourceCache;
    private readonly ReviewCheckpointCoordinator? checkpointCoordinator;
    private readonly Action<ReviewPerformanceStage, ReviewPerformanceSource, RecordingReviewWindow, double> recordWindow;
    private readonly object sourceChunkLoadGate = new();
    private readonly Dictionary<string, ReviewSourceChunkWork> sourceChunkLoads = [];
    internal const int MaximumConcurrentSourceBuilds = 2;
    private readonly SemaphoreSlim sourceBuildSlots = new(MaximumConcurrentSourceBuilds, MaximumConcurrentSourceBuilds);
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private ReviewFilterContract? filterContract;
    private int disposed;
    private double DurationSeconds => reader.DurationSeconds;

    public ReviewSourceLoader(IRecordingReviewReader reader, IRecordingReviewFilter? filter,
        RecordingReviewWindowCache cache,
        Action<ReviewPerformanceStage, ReviewPerformanceSource, RecordingReviewWindow, double> recordWindow,
        ReviewFilteredSourceChunkCache? filteredSourceCache = null)
    {
        this.reader = reader;
        this.filter = filter;
        this.cache = cache;
        this.recordWindow = recordWindow;
        this.filteredSourceCache = filteredSourceCache ?? new();
        lifetimeToken = lifetime.Token;
        if (filter is not null)
            checkpointCoordinator = new(reader, filter, new ReviewCheckpointAnchorCache());
    }
    public async Task<ReviewSourceLoadResult> LoadAsync(
        double startSeconds,
        double endSeconds,
        RecordingReviewFilterSettings settings,
        CancellationToken token,
        Action<ReviewPreparationState>? progress = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (filter is null)
        {
            return new ReviewSourceLoadResult(await ReadRawAsync(startSeconds, endSeconds, token), false,
                ReviewPerformanceSource.Disk);
        }

        ReviewFilterContract contract;
        try
        {
            contract = filterContract ??= await filter.GetContractAsync(token);
        }
        catch (HttpRequestException)
        {
            return new ReviewSourceLoadResult(await ReadRawAsync(startSeconds, endSeconds, token), true,
                ReviewPerformanceSource.RawFallback);
        }
        catch (TaskCanceledException) when (!token.IsCancellationRequested)
        {
            return new ReviewSourceLoadResult(await ReadRawAsync(startSeconds, endSeconds, token), true,
                ReviewPerformanceSource.RawFallback);
        }

        var firstChunk = (int)Math.Floor(startSeconds / FilteredSourceChunkSeconds);
        var lastChunk = (int)Math.Floor(Math.Max(startSeconds, endSeconds - 0.000_001) / FilteredSourceChunkSeconds);
        var chunks = new List<RecordingReviewWindow>(lastChunk - firstChunk + 1);
        for (var chunkIndex = firstChunk; chunkIndex <= lastChunk; chunkIndex++)
        {
            token.ThrowIfCancellationRequested();
            chunks.Add((await LoadFilteredSourceChunkAsync(chunkIndex, contract, settings, token, progress)).Window);
        }

        PrefetchAdjacentChunks(lastChunk + 1, lastChunk + 1, contract, settings);

        return new ReviewSourceLoadResult(CombineChunks(startSeconds, endSeconds, chunks), false,
            ReviewPerformanceSource.FilterService, contract);
    }

    private void PrefetchAdjacentChunks(
        int fromChunkIndex,
        int toChunkIndexInclusive,
        ReviewFilterContract contract,
        RecordingReviewFilterSettings settings)
    {
        for (var chunkIndex = fromChunkIndex; chunkIndex <= toChunkIndexInclusive; chunkIndex++)
        {
            var startSeconds = chunkIndex * FilteredSourceChunkSeconds;
            if (startSeconds >= DurationSeconds) return;
            var durationSeconds = Math.Min(FilteredSourceChunkSeconds, DurationSeconds - startSeconds);
            _ = PrefetchChunkAfterForegroundAsync(
                chunkIndex, startSeconds, durationSeconds, settings, contract);
        }
    }

    private async Task PrefetchChunkAfterForegroundAsync(
        int chunkIndex,
        double startSeconds,
        double durationSeconds,
        RecordingReviewFilterSettings settings,
        ReviewFilterContract contract)
    {
        try
        {
            await Task.Delay(250, lifetimeToken);
            await GetOrStartFilteredSourceChunkBuildAsync(
                CreateChunkKey(chunkIndex, settings, contract),
                startSeconds,
                durationSeconds,
                settings,
                contract,
                lifetimeToken);
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
    }

    private ReviewFilteredSourceChunkKey CreateChunkKey(
        int chunkIndex,
        RecordingReviewFilterSettings settings,
        ReviewFilterContract contract)
    {
        var startSeconds = chunkIndex * FilteredSourceChunkSeconds;
        var durationSeconds = Math.Min(FilteredSourceChunkSeconds, DurationSeconds - startSeconds);
        var sampleOffset = checked((long)Math.Floor(startSeconds * reader.Manifest.SamplingRateHz));
        var sampleCount = checked((int)Math.Ceiling(durationSeconds * reader.Manifest.SamplingRateHz));
        var usesExactCheckpointHistory = filter is HttpRecordingReviewFilter;
        var warmupSampleCount = usesExactCheckpointHistory ? 0 : checked((int)Math.Ceiling(
            Math.Min(startSeconds, GetFilterWarmupSeconds(settings)) * reader.Manifest.SamplingRateHz));
        return new ReviewFilteredSourceChunkKey(
            reader.Manifest.SessionId,
            ReviewFilteredSourceChunkKey.CreateManifestFingerprint(reader.Manifest),
            reader.Manifest.SamplingRateHz,
            ReviewFilteredSourceChunkKey.CreateChannelSchemaFingerprint(reader.Manifest),
            settings,
            contract,
            sampleOffset,
            sampleCount,
            warmupSampleCount,
            usesExactCheckpointHistory
                ? "recorded-segments-v2;exact-checkpoint-history;causal-state-never-crosses-gap"
                : "recorded-segments-v1;causal-state-never-crosses-gap");
    }

    private async Task<ReviewFilteredSourceChunk> LoadFilteredSourceChunkAsync(
        int chunkIndex,
        ReviewFilterContract contract,
        RecordingReviewFilterSettings settings,
        CancellationToken token,
        Action<ReviewPreparationState>? progress)
    {
        var startSeconds = chunkIndex * FilteredSourceChunkSeconds;
        var durationSeconds = Math.Min(FilteredSourceChunkSeconds, DurationSeconds - startSeconds);
        var sampleOffset = checked((long)Math.Floor(startSeconds * reader.Manifest.SamplingRateHz));
        var sampleCount = checked((int)Math.Ceiling(durationSeconds * reader.Manifest.SamplingRateHz));
        var usesExactCheckpointHistory = filter is HttpRecordingReviewFilter;
        var warmupSampleCount = usesExactCheckpointHistory ? 0 : checked((int)Math.Ceiling(
            Math.Min(startSeconds, GetFilterWarmupSeconds(settings)) * reader.Manifest.SamplingRateHz));
        var key = CreateChunkKey(chunkIndex, settings, contract);
        var work = await GetOrStartFilteredSourceChunkBuildAsync(key, startSeconds, durationSeconds, settings, contract, token);
        return await work.WaitAsync(token, progress);
    }

    private async Task<ReviewSourceChunkWork> GetOrStartFilteredSourceChunkBuildAsync(
        ReviewFilteredSourceChunkKey key,
        double startSeconds,
        double durationSeconds,
        RecordingReviewFilterSettings settings,
        ReviewFilterContract contract,
        CancellationToken token)
    {
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(token, lifetimeToken);
        admission.Token.ThrowIfCancellationRequested();
        lock (sourceChunkLoadGate)
        {
            if (sourceChunkLoads.TryGetValue(key.Fingerprint, out var existing) &&
                existing.IsReusable)
            {
                return existing;
            }
        }

        await sourceBuildSlots.WaitAsync(admission.Token);
        var ownsSlot = true;
        try
        {
            lock (sourceChunkLoadGate)
            {
                admission.Token.ThrowIfCancellationRequested();
                if (sourceChunkLoads.TryGetValue(key.Fingerprint, out var existing) && existing.IsReusable)
                    return existing;

                var work = new ReviewSourceChunkWork((buildToken, progress) =>
                    BuildFilteredSourceChunkAsync(key, startSeconds, durationSeconds, settings, contract, buildToken, progress),
                    lifetimeToken);
                sourceChunkLoads[key.Fingerprint] = work;
                _ = work.Task.ContinueWith(completed =>
                {
                    lock (sourceChunkLoadGate)
                    {
                        if (sourceChunkLoads.TryGetValue(key.Fingerprint, out var current) && ReferenceEquals(current, work))
                            sourceChunkLoads.Remove(key.Fingerprint);
                    }
                    sourceBuildSlots.Release();
                }, TaskScheduler.Default);
                ownsSlot = false;
                return work;
            }
        }
        finally
        {
            if (ownsSlot) sourceBuildSlots.Release();
        }
    }

    private async Task<ReviewFilteredSourceChunk> BuildFilteredSourceChunkAsync(
        ReviewFilteredSourceChunkKey key,
        double startSeconds,
        double durationSeconds,
        RecordingReviewFilterSettings settings,
        ReviewFilterContract contract,
        CancellationToken token,
        Action<ReviewPreparationState> progress)
    {
        var started = Stopwatch.GetTimestamp();
        var cached = await filteredSourceCache.TryReadAsync(key, token);
        if (cached is not null)
        {
            RecordWindow(ReviewPerformanceStage.FilteredSource, ReviewPerformanceSource.FilterCache,
                cached.Window, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return cached;
        }

        var raw = await ReadRawAsync(startSeconds, startSeconds + durationSeconds, token);
        if (raw.Segments.Count == 0)
        {
            return new ReviewFilteredSourceChunk(raw, null);
        }

        if (filter is not HttpRecordingReviewFilter)
        {
            var warmup = await ReadWarmupAsync(startSeconds, settings, token);
            var legacyFiltered = await filter!.ApplyWithWarmupAsync(
                warmup,
                raw,
                reader.Manifest,
                settings,
                token);
            await filteredSourceCache.StoreAsync(key, legacyFiltered, token);
            RecordWindow(ReviewPerformanceStage.FilteredSource, ReviewPerformanceSource.FilterService,
                legacyFiltered, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return new ReviewFilteredSourceChunk(legacyFiltered, null);
        }

        var coordinated = await checkpointCoordinator!.FilterAsync(raw, settings, contract, token, progress)
            ?? throw new InvalidOperationException("HTTP 回溯滤波器没有提供 checkpoint 会话。");
        var filtered = coordinated.Window;
        token.ThrowIfCancellationRequested();
        await filteredSourceCache.StoreAsync(key, filtered, coordinated.Checkpoint, token);
        RecordWindow(ReviewPerformanceStage.FilteredSource, ReviewPerformanceSource.FilterService,
            filtered, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return new ReviewFilteredSourceChunk(filtered, coordinated.Checkpoint);
    }

    private async Task<RecordingReviewWindow> ReadRawAsync(double startSeconds, double endSeconds, CancellationToken token)
    {
        if (cache.TryGetRaw(startSeconds, endSeconds, out var cached))
        {
            RecordWindow(ReviewPerformanceStage.RawRead, ReviewPerformanceSource.MemoryCache,
                cached, 0);
            return cached;
        }

        var started = Stopwatch.GetTimestamp();
        var window = await reader.ReadWindowAsync(startSeconds, endSeconds - startSeconds, token);
        token.ThrowIfCancellationRequested();
        cache.StoreRaw(window);
        RecordWindow(ReviewPerformanceStage.RawRead, ReviewPerformanceSource.Disk,
            window, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return window;
    }

    private static RecordingReviewWindow CombineChunks(
        double requestedStartSeconds,
        double requestedEndSeconds,
        IReadOnlyList<RecordingReviewWindow> chunks)
    {
        var segments = chunks.SelectMany(chunk => chunk.Segments).ToArray();
        if (segments.Length == 0)
        {
            return new RecordingReviewWindow(requestedStartSeconds, requestedStartSeconds, requestedEndSeconds, []);
        }

        return new RecordingReviewWindow(
            requestedStartSeconds,
            chunks.First(chunk => chunk.Segments.Count > 0).ActualStartSeconds,
            chunks.Last(chunk => chunk.Segments.Count > 0).ActualEndSeconds,
            segments);
    }

    private async Task<RecordingReviewWindow?> ReadWarmupAsync(
        double cacheStartSeconds,
        RecordingReviewFilterSettings settings,
        CancellationToken token)
    {
        var duration = Math.Min(cacheStartSeconds, GetFilterWarmupSeconds(settings));
        if (duration <= 0)
        {
            return null;
        }

        var start = cacheStartSeconds - duration;
        if (cache.TryGetRaw(start, cacheStartSeconds, out var cached))
        {
            return cached;
        }

        var warmup = await reader.ReadWindowAsync(start, duration, token);
        token.ThrowIfCancellationRequested();
        cache.StoreRaw(warmup);
        return warmup;
    }

    private static double GetFilterWarmupSeconds(RecordingReviewFilterSettings settings) => Math.Clamp(
        3d / settings.HighPassHz,
        MinimumFilterWarmupSeconds,
        MaximumFilterWarmupSeconds);


    private void RecordWindow(ReviewPerformanceStage stage, ReviewPerformanceSource source,
        RecordingReviewWindow window, double elapsed) => recordWindow(stage, source, window, elapsed);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        lifetime.Dispose();
        if (checkpointCoordinator is not null) await checkpointCoordinator.DisposeAsync();
    }
}
