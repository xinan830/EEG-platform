using System.IO;
using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Projects.Domain;

public static class ResearchProjectStatuses
{
    public const string All = "全部状态";
    public const string InProgress = "进行中";
    public const string Completed = "已完成";
    public const string Paused = "暂停";

    public static readonly IReadOnlyList<string> FilterOptions = [All, InProgress, Completed, Paused];

    public static string Normalize(string? status) => status switch
    {
        Completed => Completed,
        Paused => Paused,
        _ => InProgress,
    };
}

public sealed record ResearchProject(
    string Id,
    string Number,
    string Name,
    string Description,
    string Purpose,
    IReadOnlyList<string> Tags,
    string Creator,
    string DirectoryPath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Status = ResearchProjectStatuses.InProgress,
    string Notes = "")
{
    [JsonIgnore]
    public int RecordingCount { get; init; }

    public string NormalizedStatus => ResearchProjectStatuses.Normalize(Status);

    public string CreatedAtText => CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string UpdatedAtText => UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string RecordingsDirectory => Path.Combine(DirectoryPath, "recordings");

    public string TagsText => Tags.Count == 0 ? "未设置" : string.Join("、", Tags);
}

public sealed record ResearchProjectDraft(
    string Name,
    string Description,
    string Purpose,
    string TagsText,
    string Creator,
    string DirectoryPath,
    string Status = ResearchProjectStatuses.InProgress,
    string Notes = "")
{
    public IReadOnlyList<string> ParseTags() => TagsText
        .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
