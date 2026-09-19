using System.Collections.ObjectModel;
using System.IO;
using BrainPlatform.Desktop.Acquisition.Storage;
using BrainPlatform.Desktop.Projects;

namespace BrainPlatform.Desktop.ViewModels;

public sealed record ProjectRecordingRow(
    string SessionId,
    string Name,
    string StartedAtText,
    string SamplingRateText,
    string ChannelCountText,
    string Status,
    string RecordingDirectory);

public sealed class ProjectWorkspaceViewModel : ObservableObject
{
    private readonly ResearchProjectStore store;
    private readonly LocalRecordingCatalog recordingCatalog;
    private readonly OperationNotificationCenter notifications;
    private ResearchProject? selectedProject;
    private string searchText = string.Empty;

    public ProjectWorkspaceViewModel(
        OperationNotificationCenter notifications,
        ResearchProjectStore? store = null,
        LocalRecordingCatalog? recordingCatalog = null)
    {
        this.notifications = notifications;
        this.store = store ?? new ResearchProjectStore();
        this.recordingCatalog = recordingCatalog ?? new LocalRecordingCatalog();
    }

    public ObservableCollection<ResearchProject> Projects { get; } = [];

    public ObservableCollection<ResearchProject> FilteredProjects { get; } = [];

    public ObservableCollection<ProjectRecordingRow> Recordings { get; } = [];

    public ResearchProject? SelectedProject
    {
        get => selectedProject;
        set
        {
            if (SetProperty(ref selectedProject, value))
            {
                RefreshRecordings();
                RaisePropertyChanged(nameof(HasSelectedProject));
            }
        }
    }

    public bool HasSelectedProject => SelectedProject is not null;

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public async Task RefreshAsync()
    {
        var selectedId = SelectedProject?.Id;
        Projects.Clear();
        foreach (var project in (await store.LoadAsync(CancellationToken.None)).OrderByDescending(item => item.CreatedAtUtc))
        {
            Projects.Add(project);
        }

        ApplyFilter();
        SelectedProject = Projects.FirstOrDefault(project => project.Id == selectedId) ?? Projects.FirstOrDefault();
    }

    public async Task<ResearchProject> SaveAsync(ResearchProjectDraft draft, ResearchProject? existing = null)
    {
        var name = draft.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("请输入项目名称。");
        }

        if (string.IsNullOrWhiteSpace(draft.DirectoryPath))
        {
            throw new InvalidOperationException("请选择项目目录。");
        }

        var directory = Path.GetFullPath(draft.DirectoryPath.Trim());

        if (Projects.Any(project => project.Id != existing?.Id &&
                                    string.Equals(Path.GetFullPath(project.DirectoryPath), directory, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("该目录已被另一个项目使用。");
        }

        if (existing is not null &&
            !string.Equals(Path.GetFullPath(existing.DirectoryPath), directory, StringComparison.OrdinalIgnoreCase) &&
            recordingCatalog.Read(existing.RecordingsDirectory).Count > 0)
        {
            throw new InvalidOperationException("项目已有采集记录，不能再更换项目目录。请新建项目或保持原目录。");
        }

        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "recordings"));
        var now = DateTimeOffset.UtcNow;
        var project = new ResearchProject(
            existing?.Id ?? Guid.NewGuid().ToString("N"),
            existing?.Number ?? CreateProjectNumber(now),
            name,
            draft.Description.Trim(),
            draft.Purpose.Trim(),
            draft.ParseTags(),
            string.IsNullOrWhiteSpace(draft.Creator) ? Environment.UserName : draft.Creator.Trim(),
            directory,
            existing?.CreatedAtUtc ?? now,
            now);
        await store.SaveAsync(project, CancellationToken.None);
        await RefreshAsync();
        SelectedProject = Projects.Single(item => item.Id == project.Id);
        notifications.PublishSuccess(existing is null ? $"已创建项目“{project.Name}”。" : $"已保存项目“{project.Name}”。");
        return project;
    }

    public async Task DeleteSelectedAsync()
    {
        var project = SelectedProject ?? throw new InvalidOperationException("请先选择项目。");
        await store.DeleteAsync(project.Id, CancellationToken.None);
        await RefreshAsync();
        notifications.PublishSuccess($"已从平台移除项目“{project.Name}”；磁盘目录和原始数据未删除。");
    }

    public void RefreshRecordings()
    {
        Recordings.Clear();
        if (SelectedProject is null)
        {
            return;
        }

        foreach (var recording in recordingCatalog.Read(SelectedProject.RecordingsDirectory))
        {
            Recordings.Add(new ProjectRecordingRow(
                recording.SessionId,
                $"采集_{recording.RecordingStartUtc.ToLocalTime():yyyyMMdd_HHmmss}",
                recording.RecordingStartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                $"{recording.SamplingRateHz} Hz",
                $"{recording.SignalChannelCount} 信号通道",
                recording.Status,
                recording.RecordingDirectory));
        }
    }

    private string CreateProjectNumber(DateTimeOffset now)
    {
        var prefix = $"P{now.ToLocalTime():yyyyMMdd}";
        var next = Projects
            .Where(project => project.Number.StartsWith(prefix, StringComparison.Ordinal))
            .Select(project => int.TryParse(project.Number[prefix.Length..], out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return $"{prefix}{next:000}";
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        FilteredProjects.Clear();
        foreach (var project in Projects.Where(project =>
                     query.Length == 0 ||
                     project.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     project.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     project.Number.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            FilteredProjects.Add(project);
        }
    }
}
