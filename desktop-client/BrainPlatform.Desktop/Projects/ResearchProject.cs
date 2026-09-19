using System.IO;

namespace BrainPlatform.Desktop.Projects;

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
    DateTimeOffset UpdatedAtUtc)
{
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
    string DirectoryPath)
{
    public IReadOnlyList<string> ParseTags() => TagsText
        .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
