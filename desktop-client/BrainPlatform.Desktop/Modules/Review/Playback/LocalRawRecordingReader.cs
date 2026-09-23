using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Review;

public sealed class LocalRawRecordingReader : IAsyncDisposable, IRecordingReviewReader, IRecordingReviewTimeline
{
    private const int BatchHeaderBytes = sizeof(long) + sizeof(int) + sizeof(int) + sizeof(long);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private readonly string recordingDirectory;
    private readonly LocalRawRecordingManifest manifest;
    private readonly LocalRawRecordingIndex index;
    private int disposed;

    private LocalRawRecordingReader(
        string recordingDirectory,
        LocalRawRecordingManifest manifest,
        LocalRawRecordingIndex index)
    {
        this.recordingDirectory = recordingDirectory;
        this.manifest = manifest;
        this.index = index;
    }

    public static async Task<LocalRawRecording> OpenAsync(
        string recordingDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recordingDirectory))
        {
            throw new RecordingReadException("RECORDING_DIRECTORY_INVALID", "记录目录不能为空。");
        }

        var root = Path.GetFullPath(recordingDirectory);
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new RecordingReadException("RECORDING_MANIFEST_MISSING", $"缺少记录清单：{manifestPath}");
        }

        LocalRawRecordingManifest manifest;
        try
        {
            await using var manifestStream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                4096,
                FileOptions.SequentialScan | FileOptions.Asynchronous);
            manifest = await JsonSerializer.DeserializeAsync<LocalRawRecordingManifest>(
                           manifestStream,
                           JsonOptions,
                           cancellationToken)
                       ?? throw new RecordingReadException(
                           "RECORDING_MANIFEST_INVALID",
                           "记录清单为空。");
        }
        catch (RecordingReadException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new RecordingReadException(
                "RECORDING_MANIFEST_INVALID",
                "记录清单格式无效。",
                exception);
        }
        catch (IOException exception)
        {
            throw new RecordingReadException(
                "RECORDING_MANIFEST_UNREADABLE",
                "记录清单无法读取。",
                exception);
        }

        ValidateManifest(manifest);
        var batches = BuildIndex(root, manifest, cancellationToken);
        var index = new LocalRawRecordingIndex(batches, payloadBytesReadDuringOpen: 0);
        var reader = new LocalRawRecordingReader(root, manifest, index);
        var gaps = ReadGaps(root, cancellationToken);
        return new LocalRawRecording(manifest, index, gaps, reader);
    }

    public async Task<RecordingReviewWindow> ReadWindowAsync(
        double startSeconds,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(startSeconds) || startSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startSeconds), "回溯起点必须是非负有限秒数。");
        }

        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "回溯窗口必须是正有限秒数。");
        }

        var firstCounter = index.FirstSampleCounter;
        var lastExclusive = checked(index.LastSampleCounter + 1L);
        var requestedStartCounter = checked(firstCounter + (long)Math.Floor(startSeconds * manifest.SamplingRateHz));
        var requestedEndCounter = checked(firstCounter + (long)Math.Ceiling(
            (startSeconds + durationSeconds) * manifest.SamplingRateHz));
        var startCounter = Math.Clamp(requestedStartCounter, firstCounter, lastExclusive);
        var endCounter = Math.Clamp(requestedEndCounter, startCounter, lastExclusive);
        var segments = new List<RecordingReviewSegment>();
        List<double>? contiguousValues = null;
        long contiguousFirstCounter = 0;
        int contiguousSampleCount = 0;
        int contiguousChannelCount = 0;
        string? openChunkPath = null;
        FileStream? openChunk = null;

        try
        {
            foreach (var batch in index.Batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var overlapStart = Math.Max(startCounter, batch.FirstSampleCounter);
                var overlapEnd = Math.Min(endCounter, checked(batch.LastSampleCounter + 1L));
                if (overlapEnd <= overlapStart)
                {
                    continue;
                }

                if (!string.Equals(openChunkPath, batch.ChunkPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (openChunk is not null)
                    {
                        await openChunk.DisposeAsync();
                    }

                    openChunk = new FileStream(
                        batch.ChunkPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        256 * 1024,
                        FileOptions.RandomAccess | FileOptions.Asynchronous);
                    openChunkPath = batch.ChunkPath;
                }

                var sampleOffset = checked((int)(overlapStart - batch.FirstSampleCounter));
                var sampleCount = checked((int)(overlapEnd - overlapStart));
                var values = await ReadPayloadSliceAsync(
                    batch,
                    sampleOffset,
                    sampleCount,
                    openChunk ?? throw new InvalidOperationException("回溯采样分块未打开。"),
                    cancellationToken);
                if (contiguousValues is null ||
                    contiguousFirstCounter + contiguousSampleCount != overlapStart ||
                    contiguousChannelCount != batch.ChannelCount)
                {
                    FlushContiguousSegment(segments, ref contiguousValues, ref contiguousFirstCounter,
                        ref contiguousSampleCount, ref contiguousChannelCount);
                    contiguousFirstCounter = overlapStart;
                    contiguousChannelCount = batch.ChannelCount;
                    contiguousValues = new List<double>(checked(sampleCount * batch.ChannelCount));
                }

                contiguousValues.AddRange(values);
                contiguousSampleCount += sampleCount;
            }

            FlushContiguousSegment(segments, ref contiguousValues, ref contiguousFirstCounter,
                ref contiguousSampleCount, ref contiguousChannelCount);
        }
        finally
        {
            if (openChunk is not null)
            {
                await openChunk.DisposeAsync();
            }
        }

        var actualStart = segments.Count == 0
            ? startSeconds
            : ToRelativeSeconds(segments[0].FirstSampleCounter);
        var actualEnd = segments.Count == 0
            ? Math.Min(startSeconds + durationSeconds, DurationSeconds)
            : ToRelativeSeconds(segments[^1].LastSampleCounter + 1L);
        return new RecordingReviewWindow(startSeconds, actualStart, actualEnd, segments);
    }

    public double DurationSeconds =>
        (index.LastSampleCounter - index.FirstSampleCounter + 1d) / manifest.SamplingRateHz;

    public LocalRawRecordingManifest Manifest => manifest;

    public long FirstSampleCounter => index.FirstSampleCounter;

    public long GetContiguousSegmentOrigin(long sampleCounter)
    {
        var position = -1;
        for (var current = 0; current < index.Batches.Count; current++)
        {
            var batch = index.Batches[current];
            if (sampleCounter >= batch.FirstSampleCounter && sampleCounter <= batch.LastSampleCounter)
            {
                position = current;
                break;
            }
        }
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(sampleCounter));
        while (position > 0 && index.Batches[position - 1].LastSampleCounter + 1L ==
               index.Batches[position].FirstSampleCounter)
            position--;
        return index.Batches[position].FirstSampleCounter;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref disposed, 1);
        return ValueTask.CompletedTask;
    }

    private async Task<double[]> ReadPayloadSliceAsync(
        RawBatchIndexEntry batch,
        int sampleOffset,
        int sampleCount,
        FileStream stream,
        CancellationToken cancellationToken)
    {
        var valueCount = checked(sampleCount * batch.ChannelCount);
        var values = ArrayPool<double>.Shared.Rent(valueCount);
        try
        {
            stream.Position = checked(batch.PayloadOffset + (long)sampleOffset * batch.ChannelCount * sizeof(double));
            var bytes = new byte[checked(valueCount * sizeof(double))];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return values[..valueCount].ToArray();
        }
        catch (EndOfStreamException exception)
        {
            throw new RecordingReadException(
                "RECORDING_CHUNK_TRUNCATED",
                $"记录分块读取不完整：{Path.GetFileName(batch.ChunkPath)}。",
                exception);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(values);
        }
    }

    private double ToRelativeSeconds(long sampleCounter) =>
        (sampleCounter - index.FirstSampleCounter) / (double)manifest.SamplingRateHz;

    private static void FlushContiguousSegment(
        ICollection<RecordingReviewSegment> segments,
        ref List<double>? values,
        ref long firstCounter,
        ref int sampleCount,
        ref int channelCount)
    {
        if (values is null || sampleCount <= 0 || channelCount <= 0)
        {
            return;
        }

        segments.Add(new RecordingReviewSegment(firstCounter, sampleCount, channelCount, values.ToArray()));
        values = null;
        firstCounter = 0;
        sampleCount = 0;
        channelCount = 0;
    }

    private static IReadOnlyList<RawBatchIndexEntry> BuildIndex(
        string root,
        LocalRawRecordingManifest manifest,
        CancellationToken cancellationToken)
    {
        var paths = Directory.EnumerateFiles(root, "samples-*.bin", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            throw new RecordingReadException("RECORDING_SAMPLES_MISSING", "记录目录没有原始样本分块。");
        }

        var batches = new List<RawBatchIndexEntry>();
        long? previousLastCounter = null;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, FileOptions.SequentialScan);
            while (stream.Position < stream.Length)
            {
                var headerOffset = stream.Position;
                if (stream.Length - headerOffset < BatchHeaderBytes)
                {
                    throw new RecordingReadException("RECORDING_CHUNK_HEADER_TRUNCATED", $"记录分块头不完整：{Path.GetFileName(path)}。");
                }

                using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                var firstCounter = reader.ReadInt64();
                var sampleCount = reader.ReadInt32();
                var channelCount = reader.ReadInt32();
                var receiveTicks = reader.ReadInt64();
                if (firstCounter < 0 || sampleCount <= 0 || channelCount != manifest.Channels.Count)
                {
                    throw new RecordingReadException("RECORDING_BATCH_HEADER_INVALID", $"记录分块头参数无效：{Path.GetFileName(path)} @ {headerOffset}。");
                }

                var entry = new RawBatchIndexEntry(
                    path,
                    headerOffset,
                    stream.Position,
                    firstCounter,
                    sampleCount,
                    channelCount,
                    new DateTimeOffset(new DateTime(receiveTicks, DateTimeKind.Utc)));
                if (previousLastCounter is not null && firstCounter <= previousLastCounter.Value)
                {
                    throw new RecordingReadException("RECORDING_COUNTER_OVERLAP", "记录分块中的 sample counter 发生重叠或倒退。");
                }

                if (entry.PayloadByteLength > stream.Length - stream.Position)
                {
                    throw new RecordingReadException("RECORDING_CHUNK_TRUNCATED", $"记录分块 payload 不完整：{Path.GetFileName(path)} @ {headerOffset}。");
                }

                stream.Position = checked(stream.Position + entry.PayloadByteLength);
                batches.Add(entry);
                previousLastCounter = entry.LastSampleCounter;
            }
        }

        return batches;
    }

    private static IReadOnlyList<AcquisitionGap> ReadGaps(string root, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, "audit.jsonl");
        if (!File.Exists(path))
        {
            return [];
        }

        var gaps = new List<AcquisitionGap>();
        foreach (var line in File.ReadLines(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var document = JsonDocument.Parse(line);
                var rootElement = document.RootElement;
                if (!rootElement.TryGetProperty("type", out var type) ||
                    !string.Equals(type.GetString(), "sample_gap", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                gaps.Add(new AcquisitionGap(
                    GetInt64(rootElement, "FirstMissingSampleCounter"),
                    GetInt64(rootElement, "LastMissingSampleCounter"),
                    GetInt64(rootElement, "MissingSampleCount"),
                    DateTimeOffset.UtcNow));
            }
            catch (JsonException)
            {
                // An invalid audit line must not make valid raw samples unreadable.
                // The raw reader still exposes counter discontinuities as segments.
            }
        }

        return gaps;
    }

    private static long GetInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;

    private static void ValidateManifest(LocalRawRecordingManifest value)
    {
        if (value.SessionId == Guid.Empty || value.SamplingRateHz <= 0 || value.Channels.Count == 0 ||
            value.Channels.Any(channel => string.IsNullOrWhiteSpace(channel.Unit)))
        {
            throw new RecordingReadException("RECORDING_MANIFEST_INVALID", "记录清单的会话、采样率或通道表无效。");
        }

        var counter = value.Channels.SingleOrDefault(channel => channel.StreamIndex == value.SampleCounterChannelIndex);
        if (counter is null || counter.Kind != AcquisitionChannelKind.SampleCounter)
        {
            throw new RecordingReadException("RECORDING_MANIFEST_INVALID", "记录清单没有有效的 sample counter 通道。");
        }

        if (!value.Channels.Select(channel => channel.StreamIndex).Distinct().SequenceEqual(
                value.Channels.Select(channel => channel.StreamIndex)))
        {
            throw new RecordingReadException("RECORDING_MANIFEST_INVALID", "记录清单的通道顺序包含重复索引。");
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(LocalRawRecordingReader));
        }
    }
}
