using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BrainPlatform.Desktop.Modules.Review.Cache;

/// <summary>
/// Disposable metadata for completed display chunks. It never makes a partial
/// chunk visible and never writes inside the immutable recording.
/// </summary>
internal sealed class ReviewDisplayLevelIndex
{
    private static readonly byte[] Magic = "BPRVDI01"u8.ToArray();
    private readonly string root;

    public ReviewDisplayLevelIndex(string? root = null)
    {
        this.root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrainPlatform", "review-display-cache");
    }

    public async Task<IReadOnlyList<ReviewDisplayLevelIndexEntry>?> TryReadAsync(
        ReviewDisplayLevelIndexKey key, CancellationToken cancellationToken)
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
            if (headerLength <= 0 || headerLength > 16 * 1024 * 1024) return null;
            var headerBytes = new byte[headerLength];
            await stream.ReadExactlyAsync(headerBytes, cancellationToken);
            var header = JsonSerializer.Deserialize<IndexHeader>(headerBytes);
            if (header is null || !string.Equals(header.KeyFingerprint, key.Fingerprint, StringComparison.Ordinal) ||
                stream.Position != stream.Length || !IsValidEntries(header.Entries))
                return null;

            return header.Entries;
        }
        catch (EndOfStreamException) { return null; }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
        catch (OverflowException) { return null; }
    }

    public async Task StoreAsync(
        ReviewDisplayLevelIndexKey key,
        IReadOnlyList<ReviewDisplayLevelIndexEntry> entries,
        CancellationToken cancellationToken)
    {
        ValidateKey(key);
        if (!IsValidEntries(entries))
            throw new ArgumentException("Display level index entries are invalid.", nameof(entries));

        var path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(new IndexHeader(key.Fingerprint, entries));
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(Magic, cancellationToken);
                await stream.WriteAsync(BitConverter.GetBytes(headerBytes.Length), cancellationToken);
                await stream.WriteAsync(headerBytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private string GetPath(ReviewDisplayLevelIndexKey key) => Path.Combine(
        root, key.Fingerprint[..2], key.Fingerprint + ".bprvdi");

    private static void ValidateKey(ReviewDisplayLevelIndexKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(key.LevelIdentity))
            throw new ArgumentException("Display level identity is required.", nameof(key));
        ArgumentOutOfRangeException.ThrowIfNegative(key.ChannelIndex);
    }

    private static bool IsValidEntries(IReadOnlyList<ReviewDisplayLevelIndexEntry>? entries)
    {
        if (entries is null || entries.Count > 1_000_000) return false;
        var previousChunk = -1;
        var previousEnd = 0L;
        foreach (var entry in entries)
        {
            if (entry.ChunkIndex <= previousChunk || entry.ChunkIndex < 0 ||
                entry.FirstSampleCounter < 0 || entry.SampleCount <= 0 || entry.BucketCount <= 0 ||
                string.IsNullOrWhiteSpace(entry.ChunkFingerprint) ||
                (previousChunk >= 0 && entry.FirstSampleCounter < previousEnd))
                return false;
            previousChunk = entry.ChunkIndex;
            previousEnd = checked(entry.FirstSampleCounter + entry.SampleCount);
        }

        return true;
    }

    private sealed record IndexHeader(string KeyFingerprint, IReadOnlyList<ReviewDisplayLevelIndexEntry> Entries);
}

internal sealed record ReviewDisplayLevelIndexKey(string LevelIdentity, int ChannelIndex = 0)
{
    public string Fingerprint { get; } = Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"review-display-level-index-v1|{LevelIdentity}|{ChannelIndex}"))).ToLowerInvariant();
}

internal sealed record ReviewDisplayLevelIndexEntry(
    int ChunkIndex,
    long FirstSampleCounter,
    int SampleCount,
    int BucketCount,
    string ChunkFingerprint);
