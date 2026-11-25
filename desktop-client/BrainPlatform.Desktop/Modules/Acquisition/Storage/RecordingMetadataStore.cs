using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrainPlatform.Desktop.Modules.Acquisition.Storage;

public sealed record RecordingSubject(string Code = "", string Name = "")
{
    public string DisplayText => string.Join(" · ", new[] { Code, Name }.Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } text
        ? text : "未关联";
}

/// <summary>Mutable management metadata, separate from raw samples and manifest identity.</summary>
public static class RecordingMetadataStore
{
    public static RecordingSubject ReadSubject(string directory)
    {
        var metadata = Read(directory);
        return new(metadata["subjectCode"]?.GetValue<string>() ?? "", metadata["subjectName"]?.GetValue<string>() ?? "");
    }

    public static async Task SaveAsync(string directory, string? displayName, RecordingSubject subject,
        CancellationToken cancellationToken = default)
    {
        var code = subject.Code.Trim();
        var name = subject.Name.Trim();
        if (code.Length > 160 || name.Length > 160)
            throw new InvalidOperationException("受试者编号和姓名不能超过 160 个字符。");
        var metadata = Read(directory);
        if (displayName is not null) metadata["displayName"] = displayName;
        metadata["subjectCode"] = code;
        metadata["subjectName"] = name;
        var path = Path.Combine(directory, "recording-display.json");
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, metadata.ToJsonString(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static JsonObject Read(string directory)
    {
        var path = Path.Combine(directory, "recording-display.json");
        if (!File.Exists(path)) return new JsonObject();
        return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException("记录管理信息格式无效，无法安全读取或修改。");
    }
}
