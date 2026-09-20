using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BrainPlatform.Desktop.Review;

/// <summary>
/// Rebuildable, completed-only review cache. Raw recording files never live
/// under this root and are never written by this class.
/// </summary>
internal sealed class ReviewFilteredSourceChunkCache
{
    private static readonly byte[] Magic = "BPRVCH01"u8.ToArray();
    private readonly string root;

    public ReviewFilteredSourceChunkCache(string? root = null)
    {
        this.root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrainPlatform", "review-cache");
    }

    public async Task<RecordingReviewWindow?> TryReadAsync(
        ReviewFilteredSourceChunkKey key,
        CancellationToken cancellationToken)
    {
        var path = GetPath(key);
        if (!File.Exists(path)) return null;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var magic = new byte[Magic.Length];
            await stream.ReadExactlyAsync(magic, cancellationToken);
            if (!magic.AsSpan().SequenceEqual(Magic)) return null;

            var headerLengthBytes = new byte[sizeof(int)];
            await stream.ReadExactlyAsync(headerLengthBytes, cancellationToken);
            var headerLength = BitConverter.ToInt32(headerLengthBytes);
            if (headerLength is <= 0 or > 1024 * 1024) return null;
            var headerBytes = new byte[headerLength];
            await stream.ReadExactlyAsync(headerBytes, cancellationToken);
            var header = JsonSerializer.Deserialize<CacheHeader>(headerBytes);
            if (header is null || !string.Equals(header.KeyFingerprint, key.Fingerprint, StringComparison.Ordinal) ||
                header.Segments.Count == 0)
                return null;

            var segments = new List<RecordingReviewSegment>(header.Segments.Count);
            foreach (var descriptor in header.Segments)
            {
                if (descriptor.SampleCount <= 0 || descriptor.ChannelCount <= 0)
                    return null;
                var valueCount = checked(descriptor.SampleCount * descriptor.ChannelCount);
                var bytes = new byte[checked(valueCount * sizeof(double))];
                await stream.ReadExactlyAsync(bytes, cancellationToken);
                var values = new double[valueCount];
                Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
                segments.Add(new RecordingReviewSegment(
                    descriptor.FirstSampleCounter, descriptor.SampleCount, descriptor.ChannelCount, values));
            }

            if (stream.Position != stream.Length) return null;
            return new RecordingReviewWindow(header.RequestedStartSeconds, header.ActualStartSeconds,
                header.ActualEndSeconds, segments);
        }
        catch (EndOfStreamException) { return null; }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    public async Task StoreAsync(
        ReviewFilteredSourceChunkKey key,
        RecordingReviewWindow window,
        CancellationToken cancellationToken)
    {
        if (window.Segments.Count == 0) return;
        var path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var header = new CacheHeader(
            key.Fingerprint,
            window.RequestedStartSeconds,
            window.ActualStartSeconds,
            window.ActualEndSeconds,
            window.Segments.Select(segment => new SegmentDescriptor(
                segment.FirstSampleCounter, segment.SampleCount, segment.ChannelCount)).ToArray());
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header);

        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(Magic, cancellationToken);
                await stream.WriteAsync(BitConverter.GetBytes(headerBytes.Length), cancellationToken);
                await stream.WriteAsync(headerBytes, cancellationToken);
                foreach (var segment in window.Segments)
                {
                    var bytes = new byte[checked(segment.SampleMajorValues.Length * sizeof(double))];
                    Buffer.BlockCopy(segment.SampleMajorValues, 0, bytes, 0, bytes.Length);
                    await stream.WriteAsync(bytes, cancellationToken);
                }
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string GetPath(ReviewFilteredSourceChunkKey key) => Path.Combine(root, key.RecordingSessionId.ToString("N"),
        key.Fingerprint[..2], key.Fingerprint + ".bprvc");

    private sealed record CacheHeader(
        string KeyFingerprint,
        double RequestedStartSeconds,
        double ActualStartSeconds,
        double ActualEndSeconds,
        IReadOnlyList<SegmentDescriptor> Segments);

    private sealed record SegmentDescriptor(long FirstSampleCounter, int SampleCount, int ChannelCount);
}

public sealed record ReviewFilterContract(string AlgorithmVersion, string Fingerprint)
{
    public static readonly ReviewFilterContract Unknown = new("unknown", "unknown");
}

/// <summary>Identity for a filtered source chunk, derived only from immutable recording facts and filter semantics.</summary>
internal sealed record ReviewFilteredSourceChunkKey(
    Guid RecordingSessionId,
    string RawManifestFingerprint,
    int SamplingRateHz,
    string ChannelSchemaFingerprint,
    RecordingReviewFilterSettings Settings,
    ReviewFilterContract FilterContract,
    long StartSampleOffset,
    int SampleCount,
    int WarmupSampleCount,
    string GapContext)
{
    public string Fingerprint { get; } = ComputeFingerprint(
        RecordingSessionId, RawManifestFingerprint, SamplingRateHz, ChannelSchemaFingerprint, Settings,
        FilterContract, StartSampleOffset, SampleCount, WarmupSampleCount, GapContext);

    public static string CreateManifestFingerprint(LocalRawRecordingManifest manifest) => Hash(string.Join("\n",
        manifest.PayloadFormat,
        manifest.EegSignalUnit,
        manifest.DeviceId,
        manifest.DeviceName,
        manifest.SamplingRateHz.ToString(System.Globalization.CultureInfo.InvariantCulture),
        string.Join("\n", manifest.Channels.OrderBy(channel => channel.StreamIndex).Select(channel =>
            $"{channel.StreamIndex}|{channel.NativeChannelIndex}|{channel.Label}|{channel.Kind}|{channel.Unit}"))));

    public static string CreateChannelSchemaFingerprint(LocalRawRecordingManifest manifest) => Hash(string.Join("\n",
        manifest.Channels.OrderBy(channel => channel.StreamIndex).Select(channel =>
            $"{channel.StreamIndex}|{channel.Label}|{channel.Kind}|{channel.Unit}")));

    private static string ComputeFingerprint(Guid sessionId, string rawManifestFingerprint, int samplingRateHz,
        string channelSchemaFingerprint, RecordingReviewFilterSettings settings, ReviewFilterContract contract,
        long startSampleOffset, int sampleCount, int warmupSampleCount, string gapContext) => Hash(string.Join("\n",
            "review-filtered-source-chunk-v1", sessionId.ToString("N"), rawManifestFingerprint, samplingRateHz,
            channelSchemaFingerprint, settings.HighPassHz.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            settings.LowPassHz.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            settings.NotchHz?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "off",
            contract.AlgorithmVersion, contract.Fingerprint, startSampleOffset, sampleCount, warmupSampleCount, gapContext));

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
