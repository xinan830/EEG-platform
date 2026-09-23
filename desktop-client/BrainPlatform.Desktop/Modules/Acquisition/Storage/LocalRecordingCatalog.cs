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
    string RecordingDirectory);

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
            return new LocalRecordingSummary(
                root.GetProperty("SessionId").GetGuid().ToString("N"),
                root.GetProperty("RecordingStartUtc").GetDateTimeOffset(),
                root.GetProperty("DeviceName").GetString() ?? "未知设备",
                root.GetProperty("SamplingRateHz").GetInt32(),
                signalChannelCount,
                streamColumnCount - signalChannelCount,
                streamColumnCount,
                ReadStatus(Path.Combine(directory, "audit.jsonl")),
                directory);
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
}
