using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BrainPlatform.Desktop.Modules.Review.Cache;

/// <summary>
/// Disposable, completed-only storage for display extrema. It never reads or
/// writes the immutable recording and keeps gap state explicit.
/// </summary>
internal sealed class ReviewDisplayLevelChunkCache
{
    private static readonly byte[] Magic = "BPRVDL02"u8.ToArray();
    private const int BucketBytes = sizeof(long) * 3 + sizeof(int) + sizeof(byte) + sizeof(double) * 2;
    private readonly string root;
    private readonly long maximumBytes;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, CachedFileMetadata> metadataByPath = new(StringComparer.OrdinalIgnoreCase);
    private long trackedBytes;
    private bool indexLoaded;

    public ReviewDisplayLevelChunkCache(string? root = null, long maximumBytes = 512L * 1024 * 1024)
    {
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        this.root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrainPlatform", "review-display-cache");
        this.maximumBytes = maximumBytes;
    }

    public async Task<ReviewDisplayLevelChunk?> TryReadAsync(
        ReviewDisplayLevelChunkKey key, CancellationToken cancellationToken)
    {
        ValidateKey(key);
        var path = GetPath(key);
        if (!File.Exists(path)) return null;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var magic = new byte[Magic.Length];
            await stream.ReadExactlyAsync(magic, cancellationToken);
            if (!magic.AsSpan().SequenceEqual(Magic)) return null;

            var headerLengthBytes = new byte[sizeof(int)];
            await stream.ReadExactlyAsync(headerLengthBytes, cancellationToken);
            var headerLength = BitConverter.ToInt32(headerLengthBytes);
            if (headerLength <= 0 || headerLength > 1024 * 1024) return null;
            var headerBytes = new byte[headerLength];
            await stream.ReadExactlyAsync(headerBytes, cancellationToken);
            var header = JsonSerializer.Deserialize<ChunkHeader>(headerBytes);
            if (header is null || !string.Equals(header.KeyFingerprint, key.Fingerprint, StringComparison.Ordinal) ||
                header.BucketCount <= 0 || header.BucketCount > 1_000_000 ||
            header.ChunkIndex != key.ChunkIndex || header.ChannelIndex != key.ChannelIndex ||
            stream.Length - stream.Position != (long)header.BucketCount * BucketBytes)
                return null;

            var buckets = new List<ReviewDisplayBucket>(header.BucketCount);
            var buffer = new byte[BucketBytes];
            for (var index = 0; index < header.BucketCount; index++)
            {
                await stream.ReadExactlyAsync(buffer, cancellationToken);
                var bucket = ReadBucket(buffer);
                if (!IsValid(bucket)) return null;
                buckets.Add(bucket);
            }

            await MarkAccessedAsync(path, cancellationToken);
            return new ReviewDisplayLevelChunk(key.LevelIdentity, key.ChunkIndex, buckets);
        }
        catch (EndOfStreamException) { return null; }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    public async Task StoreAsync(
        ReviewDisplayLevelChunkKey key,
        ReviewDisplayLevelChunk chunk,
        CancellationToken cancellationToken)
    {
        ValidateKey(key);
        if (!string.Equals(chunk.LevelIdentity, key.LevelIdentity, StringComparison.Ordinal) ||
            chunk.ChunkIndex != key.ChunkIndex || chunk.Buckets.Count == 0 || chunk.Buckets.Count > 1_000_000 ||
            chunk.Buckets.Any(bucket => !IsValid(bucket)))
            throw new ArgumentException("Display level chunk does not match its key or contains invalid buckets.", nameof(chunk));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = GetPath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var headerBytes = JsonSerializer.SerializeToUtf8Bytes(new ChunkHeader(
                key.Fingerprint, key.ChunkIndex, key.ChannelIndex, chunk.Buckets.Count));
            try
            {
                await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(Magic, cancellationToken);
                    await stream.WriteAsync(BitConverter.GetBytes(headerBytes.Length), cancellationToken);
                    await stream.WriteAsync(headerBytes, cancellationToken);
                    foreach (var bucket in chunk.Buckets)
                        await stream.WriteAsync(WriteBucket(bucket), cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            EnsureIndexLoaded();
            UpdateMetadataAfterStore(path);
            EnforceBounds();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task MarkAccessedAsync(string path, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureIndexLoaded();
            if (metadataByPath.TryGetValue(path, out var metadata))
                metadataByPath[path] = metadata with { LastAccessUtc = DateTime.UtcNow };
        }
        finally { gate.Release(); }
    }

    private void EnsureIndexLoaded()
    {
        if (indexLoaded) return;
        if (Directory.Exists(root))
        {
            foreach (var path in Directory.EnumerateFiles(root, "*.bprvd", SearchOption.AllDirectories))
            {
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                metadataByPath[path] = new CachedFileMetadata(
                    path, info.Length, info.LastAccessTimeUtc, info.LastWriteTimeUtc);
                trackedBytes += info.Length;
            }
        }
        indexLoaded = true;
    }

    private void UpdateMetadataAfterStore(string path)
    {
        if (metadataByPath.Remove(path, out var removed)) trackedBytes -= removed.Length;
        var info = new FileInfo(path);
        metadataByPath[path] = new CachedFileMetadata(path, info.Length, DateTime.UtcNow, DateTime.UtcNow);
        trackedBytes += info.Length;
    }

    private void EnforceBounds()
    {
        if (trackedBytes <= maximumBytes) return;
        foreach (var metadata in metadataByPath.Values
                     .OrderBy(item => item.LastAccessUtc)
                     .ThenBy(item => item.LastWriteUtc)
                     .ToArray())
        {
            if (trackedBytes <= maximumBytes) break;
            try
            {
                if (File.Exists(metadata.Path)) File.Delete(metadata.Path);
            }
            catch (IOException) { continue; }
            if (File.Exists(metadata.Path)) continue;
            metadataByPath.Remove(metadata.Path);
            trackedBytes -= metadata.Length;
        }
    }

    private sealed record CachedFileMetadata(
        string Path, long Length, DateTime LastAccessUtc, DateTime LastWriteUtc);

    private string GetPath(ReviewDisplayLevelChunkKey key) => Path.Combine(
        root, key.Fingerprint[..2], key.Fingerprint + ".bprvd");

    private static void ValidateKey(ReviewDisplayLevelChunkKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(key.LevelIdentity))
            throw new ArgumentException("Display level identity is required.", nameof(key));
        ArgumentOutOfRangeException.ThrowIfNegative(key.ChunkIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(key.ChannelIndex);
    }

    private static bool IsValid(ReviewDisplayBucket bucket)
    {
        if (bucket.IsGap)
        {
            return bucket.MinSampleCounter < 0 && bucket.MaxSampleCounter < 0 &&
                double.IsNaN(bucket.MinVolts) && double.IsNaN(bucket.MaxVolts);
        }

        if (bucket.SampleCount <= 0 || bucket.FirstSampleCounter > long.MaxValue - bucket.SampleCount)
            return false;
        var endExclusive = bucket.FirstSampleCounter + bucket.SampleCount;
        return bucket.MinSampleCounter >= bucket.FirstSampleCounter &&
            bucket.MaxSampleCounter >= bucket.FirstSampleCounter &&
            bucket.MinSampleCounter < endExclusive && bucket.MaxSampleCounter < endExclusive &&
            double.IsFinite(bucket.MinVolts) && double.IsFinite(bucket.MaxVolts) &&
            bucket.MinVolts <= bucket.MaxVolts;
    }

    private static byte[] WriteBucket(ReviewDisplayBucket bucket)
    {
        using var stream = new MemoryStream(BucketBytes);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(bucket.FirstSampleCounter);
        writer.Write(bucket.SampleCount);
        writer.Write(bucket.IsGap);
        writer.Write(bucket.MinSampleCounter);
        writer.Write(bucket.MaxSampleCounter);
        writer.Write(bucket.MinVolts);
        writer.Write(bucket.MaxVolts);
        return stream.ToArray();
    }

    private static ReviewDisplayBucket ReadBucket(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        return new ReviewDisplayBucket(reader.ReadInt64(), reader.ReadInt32(), reader.ReadBoolean(),
            reader.ReadInt64(), reader.ReadInt64(), reader.ReadDouble(), reader.ReadDouble());
    }

    private sealed record ChunkHeader(string KeyFingerprint, int ChunkIndex, int ChannelIndex, int BucketCount);
}

internal sealed record ReviewDisplayLevelChunkKey(string LevelIdentity, int ChunkIndex, int ChannelIndex = 0)
{
    public string Fingerprint { get; } = Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"review-display-level-chunk-v2|{LevelIdentity}|{ChunkIndex}|{ChannelIndex}"))).ToLowerInvariant();
}

internal sealed record ReviewDisplayLevelChunk(
    string LevelIdentity,
    int ChunkIndex,
    IReadOnlyList<ReviewDisplayBucket> Buckets);

internal sealed record ReviewDisplayBucket(
    long FirstSampleCounter,
    int SampleCount,
    bool IsGap,
    long MinSampleCounter,
    long MaxSampleCounter,
    double MinVolts,
    double MaxVolts);
