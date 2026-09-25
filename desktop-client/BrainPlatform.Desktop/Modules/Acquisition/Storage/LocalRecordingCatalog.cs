using System.Text.Json;
using System.IO;

namespace BrainPlatform.Desktop.Modules.Acquisition.Storage;

/// <summary>
/// Reads local recording indexes only. It never opens raw sample chunks or
/// performs EEG computation.
/// </summary>
public sealed record LocalRecordingSummary(
    string SessionId,
    DateTimeOffset RecordingStartUtc,
    string DeviceName,
    int SamplingRateHz,
    int SignalChannelCount,
    int AuxiliaryChannelCount,
    int StreamColumnCount,
    string Status,
    string RecordingDirectory,
    double? EffectiveDurationSeconds)
{
    public string DurationText => EffectiveDurationSeconds is { } seconds &&
                                  double.IsFinite(seconds) && seconds >= 0
        ? FormatDuration(seconds)
        : "--";

    private static string FormatDuration(double seconds)
    {
        if (seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            return "--";
        }

        var duration = TimeSpan.FromSeconds(seconds);
        return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}.{duration.Milliseconds:000}";
    }
}

public sealed class LocalRecordingCatalog
{
    public IReadOnlyList<LocalRecordingSummary> Read(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return [];
        }

        var records = new List<LocalRecordingSummary>();
        foreach (var manifestPath in Directory.EnumerateFiles(rootDirectory, "manifest.json", SearchOption.AllDirectories))
        {
            var record = TryReadManifest(manifestPath);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        return records.OrderByDescending(record => record.RecordingStartUtc).ToArray();
    }

    private static LocalRecordingSummary? TryReadManifest(string manifestPath)
    {
        try
        {
            using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var directory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
            var channels = root.GetProperty("Channels");
            var signalChannelCount = channels.EnumerateArray().Count(channel => IsSignalChannel(channel.GetProperty("Kind")));
            var streamColumnCount = channels.GetArrayLength();
            var samplingRateHz = root.GetProperty("SamplingRateHz").GetInt32();
            var effectiveDurationSeconds = TryReadSummary(
                Path.Combine(directory, "recording-summary.json"),
                samplingRateHz) ?? TryScanChunkDuration(directory, samplingRateHz, streamColumnCount);
            return new LocalRecordingSummary(
                root.GetProperty("SessionId").GetGuid().ToString("N"),
                root.GetProperty("RecordingStartUtc").GetDateTimeOffset(),
                root.GetProperty("DeviceName").GetString() ?? "未知设备",
                samplingRateHz,
                signalChannelCount,
                streamColumnCount - signalChannelCount,
                streamColumnCount,
                ReadStatus(Path.Combine(directory, "audit.jsonl")),
                directory,
                effectiveDurationSeconds);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static bool IsSignalChannel(JsonElement kind) => kind.ValueKind switch
    {
        JsonValueKind.Number => kind.GetInt32() is (int)Acquisition.Contracts.AcquisitionChannelKind.Reference or
            (int)Acquisition.Contracts.AcquisitionChannelKind.Bipolar,
        JsonValueKind.String => kind.GetString() is nameof(Acquisition.Contracts.AcquisitionChannelKind.Reference) or
            nameof(Acquisition.Contracts.AcquisitionChannelKind.Bipolar),
        _ => false,
    };

    private static string ReadStatus(string auditPath)
    {
        if (!File.Exists(auditPath))
        {
            return "记录中或审计文件缺失";
        }

        try
        {
            using var stream = new FileStream(auditPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string? latestType = null;
            while (reader.ReadLine() is { } line)
            {
                using var entry = JsonDocument.Parse(line);
                if (entry.RootElement.TryGetProperty("type", out var type))
                {
                    latestType = type.GetString();
                }
            }

            return latestType switch
            {
                "completed" => "已完成",
                "aborted" => "已中止",
                _ => "记录中",
            };
        }
        catch (IOException)
        {
            return "审计读取中";
        }
        catch (JsonException)
        {
            return "审计文件无效";
        }
    }

    private static double? TryReadSummary(string summaryPath, int manifestSamplingRateHz)
    {
        if (!File.Exists(summaryPath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(summaryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var sampleCount = root.TryGetProperty("effective_sample_count", out var count)
                ? count.GetInt64()
                : root.TryGetProperty("EffectiveSampleCount", out count) ? count.GetInt64() : -1;
            var samplingRate = root.TryGetProperty("sampling_rate_hz", out var rate)
                ? rate.GetInt32()
                : root.TryGetProperty("SamplingRateHz", out rate) ? rate.GetInt32() : manifestSamplingRateHz;
            if (sampleCount < 0 || samplingRate <= 0 || samplingRate != manifestSamplingRateHz)
            {
                return null;
            }

            // The count and manifest rate are authoritative; a stored duration
            // must not override them when an index is stale or corrupt.
            return sampleCount / (double)samplingRate;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static double? TryScanChunkDuration(string directory, int samplingRateHz, int manifestChannelCount)
    {
        if (samplingRateHz <= 0 || manifestChannelCount <= 0)
        {
            return null;
        }

        try
        {
            long sampleCount = 0;
            var foundChunk = false;
            foreach (var path in Directory.EnumerateFiles(directory, "samples-*.bin", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                foundChunk = true;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new BinaryReader(stream);
                while (stream.Position < stream.Length)
                {
                    if (stream.Length - stream.Position < 24)
                    {
                        return null;
                    }

                    _ = reader.ReadInt64();
                    var batchSampleCount = reader.ReadInt32();
                    var channelCount = reader.ReadInt32();
                    _ = reader.ReadInt64();
                    if (batchSampleCount <= 0 || channelCount != manifestChannelCount)
                    {
                        return null;
                    }

                    var payloadBytes = checked((long)batchSampleCount * channelCount * sizeof(double));
                    if (stream.Length - stream.Position < payloadBytes)
                    {
                        return null;
                    }

                    stream.Seek(payloadBytes, SeekOrigin.Current);
                    sampleCount = checked(sampleCount + batchSampleCount);
                }
            }

            return foundChunk ? sampleCount / (double)samplingRateHz : null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
