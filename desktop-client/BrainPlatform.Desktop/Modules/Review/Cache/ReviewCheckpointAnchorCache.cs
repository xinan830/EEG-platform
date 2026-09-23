using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Review.Cache;

internal sealed record ReviewCheckpointAnchorGroupKey(
    Guid RecordingSessionId,
    string RawManifestFingerprint,
    int SamplingRateHz,
    string ChannelSchemaFingerprint,
    string SignalUnit,
    RecordingReviewFilterSettings Settings,
    ReviewFilterContract FilterContract)
{
    private const string SemanticsVersion = "review-checkpoint-anchor-v1";

    public string Fingerprint { get; } = Hash(string.Join("\n",
        SemanticsVersion,
        RecordingSessionId.ToString("N"),
        RawManifestFingerprint,
        SamplingRateHz.ToString(CultureInfo.InvariantCulture),
        ChannelSchemaFingerprint,
        SignalUnit,
        Settings.HighPassHz.ToString("R", CultureInfo.InvariantCulture),
        Settings.LowPassHz.ToString("R", CultureInfo.InvariantCulture),
        Settings.NotchHz?.ToString("R", CultureInfo.InvariantCulture) ?? "off",
        FilterContract.AlgorithmVersion,
        FilterContract.Fingerprint,
        FilterContract.CheckpointVersion));

    public static ReviewCheckpointAnchorGroupKey Create(
        LocalRawRecordingManifest manifest,
        RecordingReviewFilterSettings settings,
        ReviewFilterContract contract) => new(
            manifest.SessionId,
            ReviewFilteredSourceChunkKey.CreateManifestFingerprint(manifest),
            manifest.SamplingRateHz,
            ReviewFilteredSourceChunkKey.CreateChannelSchemaFingerprint(manifest),
            manifest.EegSignalUnit,
            settings,
            contract);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal sealed record ReviewCheckpointAnchor(
    string GroupFingerprint,
    long SegmentOriginSampleCounter,
    long NextSampleCounter,
    string CheckpointB64);

/// <summary>Bounded, rebuildable storage for completed Python filter states.</summary>
internal sealed class ReviewCheckpointAnchorCache
{
    private const string FileExtension = ".bpra";
    private const int FormatVersion = 1;
    private readonly string root;
    private readonly int maximumAnchorCount;
    private readonly long maximumBytes;
    private readonly SemaphoreSlim gate = new(1, 1);

    public ReviewCheckpointAnchorCache(
        string? root = null,
        int maximumAnchorCount = 4096,
        long maximumBytes = 64L * 1024 * 1024)
    {
        if (maximumAnchorCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumAnchorCount));
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        this.root = root ?? Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "BrainPlatform", "review-cache", "anchors");
        this.maximumAnchorCount = maximumAnchorCount;
        this.maximumBytes = maximumBytes;
    }

    public async Task<ReviewCheckpointAnchor?> FindNearestAsync(
        ReviewCheckpointAnchorGroupKey group,
        long segmentOriginSampleCounter,
        long targetNextSampleCounter,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var directory = GetGroupDirectory(group);
            if (!Directory.Exists(directory)) return null;
            foreach (var path in Directory.EnumerateFiles(directory, $"*{FileExtension}")
                         .OrderByDescending(ParseNextSampleCounter))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var next = ParseNextSampleCounter(path);
                if (next > targetNextSampleCounter) continue;
                var anchor = await TryReadAsync(path, cancellationToken);
                if (anchor is null || anchor.GroupFingerprint != group.Fingerprint ||
                    anchor.SegmentOriginSampleCounter != segmentOriginSampleCounter ||
                    anchor.NextSampleCounter != next)
                    continue;
                File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
                return anchor;
            }
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task StoreAsync(
        ReviewCheckpointAnchorGroupKey group,
        long segmentOriginSampleCounter,
        long nextSampleCounter,
        string checkpointB64,
        CancellationToken cancellationToken)
    {
        if (nextSampleCounter <= segmentOriginSampleCounter)
            throw new ArgumentOutOfRangeException(nameof(nextSampleCounter));
        if (string.IsNullOrWhiteSpace(checkpointB64))
            throw new ArgumentException("Checkpoint must not be empty.", nameof(checkpointB64));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var directory = GetGroupDirectory(group);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{nextSampleCounter.ToString(CultureInfo.InvariantCulture)}{FileExtension}");
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var payload = JsonSerializer.SerializeToUtf8Bytes(new AnchorFile(
                FormatVersion, group.Fingerprint, segmentOriginSampleCounter, nextSampleCounter, checkpointB64));
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                                 FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(payload, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            EnforceBounds(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ReviewCheckpointAnchor?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > 4 * 1024 * 1024) return null;
            var value = await JsonSerializer.DeserializeAsync<AnchorFile>(stream, cancellationToken: cancellationToken);
            return value is { Version: FormatVersion } && !string.IsNullOrWhiteSpace(value.CheckpointB64)
                ? new ReviewCheckpointAnchor(value.GroupFingerprint, value.SegmentOriginSampleCounter,
                    value.NextSampleCounter, value.CheckpointB64)
                : null;
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    private void EnforceBounds(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root)) return;
        var files = Directory.EnumerateFiles(root, $"*{FileExtension}", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastAccessTimeUtc)
            .ToArray();
        long retainedBytes = 0;
        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var retain = index < maximumAnchorCount && retainedBytes + files[index].Length <= maximumBytes;
            if (retain) retainedBytes += files[index].Length;
            else files[index].Delete();
        }
    }

    private string GetGroupDirectory(ReviewCheckpointAnchorGroupKey group) =>
        Path.Combine(root, group.RecordingSessionId.ToString("N"), group.Fingerprint);

    private static long ParseNextSampleCounter(string path) =>
        long.TryParse(Path.GetFileNameWithoutExtension(path), NumberStyles.None, CultureInfo.InvariantCulture,
            out var value) ? value : long.MinValue;

    private sealed record AnchorFile(
        int Version,
        string GroupFingerprint,
        long SegmentOriginSampleCounter,
        long NextSampleCounter,
        string CheckpointB64);
}
