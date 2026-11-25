using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrainPlatform.Desktop.Modules.Acquisition.Storage;

public sealed record RecordingSubject(string Code = "", string Name = "", string? SubjectId = null,
    string? Sex = null, int? AgeAtRecordingYears = null)
{
    public string DisplayText => string.Join(" · ", new[] { Code, Name }.Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } text
        ? text : "未关联";
    public string DetailsText => $"{DisplayText}\n性别：{Sex switch { "male" => "男", "female" => "女", "other" => "其他", _ => "未填写" }}\n采集时年龄：{(AgeAtRecordingYears is { } age ? $"{age} 岁" : "未填写")}";

    public void Validate()
    {
        if (Code.Trim().Length > 160 || Name.Trim().Length > 160)
            throw new InvalidOperationException("受试者编号和姓名不能超过 160 个字符。");
        if (Sex is not (null or "male" or "female" or "other"))
            throw new InvalidOperationException("请选择有效的性别，或留空。");
        if (AgeAtRecordingYears is < 0 or > 150)
            throw new InvalidOperationException("采集时年龄须为 0–150 之间的整数，或留空。");
        if (SubjectId is null && string.IsNullOrWhiteSpace(Code) && string.IsNullOrWhiteSpace(Name) && Sex is not null)
            throw new InvalidOperationException("填写性别时请同时填写受试者编号或姓名。");
    }
}

/// <summary>Mutable management metadata, separate from raw samples and manifest identity.</summary>
public static class RecordingMetadataStore
{
    public static RecordingSubject ReadSubject(string directory)
    {
        var metadata = Read(directory);
        var subject = new RecordingSubject(metadata["subjectCode"]?.GetValue<string>() ?? "", metadata["subjectName"]?.GetValue<string>() ?? "",
            metadata["subjectId"]?.GetValue<string>(), metadata["subjectSex"]?.GetValue<string>(),
            metadata["ageAtRecordingYears"]?.GetValue<int>());
        subject.Validate();
        return subject;
    }

    public static async Task SaveAsync(string directory, string? displayName, RecordingSubject subject,
        CancellationToken cancellationToken = default)
    {
        var code = subject.Code.Trim();
        var name = subject.Name.Trim();
        subject.Validate();
        var metadata = Read(directory);
        if (displayName is not null) metadata["displayName"] = displayName;
        metadata["subjectCode"] = code;
        metadata["subjectName"] = name;
        metadata["subjectId"] = subject.SubjectId;
        metadata["subjectSex"] = subject.Sex;
        metadata["ageAtRecordingYears"] = subject.AgeAtRecordingYears;
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
