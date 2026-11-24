using System.IO;
using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Projects.Stores;

public sealed class ResearchProjectStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string path;

    public ResearchProjectStore(string? path = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrainPlatform",
            "research-projects.json");
    }

    public async Task<IReadOnlyList<ResearchProject>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<ResearchProject>>(
                   stream,
                   SerializerOptions,
                   cancellationToken)
               ?? [];
    }

    public async Task SaveAsync(ResearchProject project, CancellationToken cancellationToken)
    {
        var projects = (await LoadAsync(cancellationToken)).ToList();
        if (projects.Any(existing => existing.Id != project.Id &&
                                    string.Equals(existing.Number, project.Number, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"项目编号“{project.Number}”已存在。");
        }
        projects.RemoveAll(item => item.Id == project.Id);
        projects.Add(project);
        await WriteAsync(projects, cancellationToken);
    }

    public async Task DeleteAsync(string projectId, CancellationToken cancellationToken)
    {
        var projects = (await LoadAsync(cancellationToken))
            .Where(project => project.Id != projectId)
            .ToArray();
        await WriteAsync(projects, cancellationToken);
    }

    private async Task WriteAsync(IReadOnlyList<ResearchProject> projects, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("项目索引路径无效。"));
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, projects, SerializerOptions, cancellationToken);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
