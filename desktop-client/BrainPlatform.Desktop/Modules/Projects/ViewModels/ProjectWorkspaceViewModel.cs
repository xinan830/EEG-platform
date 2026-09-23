using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Projects.ViewModels;

public sealed record ProjectRecordingRow(
    string SessionId,
    string Name,
    string StartedAtText,
    string SamplingRateText,
    string ChannelCountText,
    string Status,
    string RecordingDirectory)
{
    public bool CanReview => Directory.Exists(RecordingDirectory) &&
                             Status is "已完成" or "已中止";
}

public sealed class ProjectWorkspaceViewModel : ObservableObject
{
    private readonly ResearchProjectStore store;
    private readonly LocalRecordingCatalog recordingCatalog;
    private readonly OperationNotificationCenter notifications;
    private ResearchProject? selectedProject;
    private ProjectRecordingRow? selectedRecording;
    private string searchText = string.Empty;
    private string projectStatusFilter = ResearchProjectStatuses.All;
    private string createdTimeFilter = "全部时间";
    private DateTime? createdFrom;
    private DateTime? createdTo;
    private int projectPageSize = 10;
    private int projectCurrentPage = 1;
    private int recordingPageSize = 10;
    private int recordingCurrentPage = 1;

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

    /// <summary>Projects visible on the current list page. FilteredProjects remains the complete filtered result.</summary>
    public ObservableCollection<ResearchProject> PagedProjects { get; } = [];

    public ObservableCollection<ProjectRecordingRow> Recordings { get; } = [];

    public ObservableCollection<ProjectRecordingRow> PagedRecordings { get; } = [];

    public IReadOnlyList<int> RecordingPageSizeOptions { get; } = [10, 20, 50];

    public IReadOnlyList<int> ProjectPageSizeOptions { get; } = [10, 20, 50];

    public int ProjectPageSize
    {
        get => projectPageSize;
        set
        {
            var normalized = ProjectPageSizeOptions.Contains(value) ? value : 10;
            if (SetProperty(ref projectPageSize, normalized))
            {
                projectCurrentPage = 1;
                RaisePropertyChanged(nameof(ProjectCurrentPage));
                ApplyProjectPage();
            }
        }
    }

    public int ProjectCurrentPage => projectCurrentPage;

    public int ProjectTotalPages => Math.Max(1, (int)Math.Ceiling(FilteredProjects.Count / (double)ProjectPageSize));

    public bool CanGoToPreviousProjectPage => ProjectCurrentPage > 1;

    public bool CanGoToNextProjectPage => ProjectCurrentPage < ProjectTotalPages;

    public int RecordingPageSize
    {
        get => recordingPageSize;
        set
        {
            var normalized = RecordingPageSizeOptions.Contains(value) ? value : 10;
            if (SetProperty(ref recordingPageSize, normalized))
            {
                recordingCurrentPage = 1;
                RaisePropertyChanged(nameof(RecordingCurrentPage));
                ApplyRecordingPage();
            }
        }
    }

    public int RecordingCurrentPage => recordingCurrentPage;

    public int RecordingTotalPages => Math.Max(1, (int)Math.Ceiling(Recordings.Count / (double)RecordingPageSize));

    public bool CanGoToPreviousRecordingPage => RecordingCurrentPage > 1;

    public bool CanGoToNextRecordingPage => RecordingCurrentPage < RecordingTotalPages;

    public IReadOnlyList<string> ProjectStatusFilterOptions => ResearchProjectStatuses.FilterOptions;

    public IReadOnlyList<string> CreatedTimeFilterOptions { get; } = ["全部时间", "今天", "近7天", "近30天", "本月"];

    public string CreatedTimeFilter
    {
        get => createdTimeFilter;
        set
        {
            var normalized = CreatedTimeFilterOptions.Contains(value) ? value : "全部时间";
            if (SetProperty(ref createdTimeFilter, normalized)) ApplyFilter();
        }
    }

    public string ProjectStatusFilter
    {
        get => projectStatusFilter;
        set
        {
            var normalized = ResearchProjectStatuses.FilterOptions.Contains(value)
                ? value
                : ResearchProjectStatuses.All;
            if (SetProperty(ref projectStatusFilter, normalized)) ApplyFilter();
        }
    }

    public DateTime? CreatedFrom
    {
        get => createdFrom;
        set
        {
            if (SetProperty(ref createdFrom, value?.Date)) ApplyFilter();
        }
    }

    public DateTime? CreatedTo
    {
        get => createdTo;
        set
        {
            if (SetProperty(ref createdTo, value?.Date)) ApplyFilter();
        }
    }

    public ProjectRecordingRow? SelectedRecording
    {
        get => selectedRecording;
        set
        {
            if (SetProperty(ref selectedRecording, value))
            {
                RaisePropertyChanged(nameof(CanReviewSelectedRecording));
            }
        }
    }

    public bool CanReviewSelectedRecording => SelectedRecording?.CanReview == true;

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
            Projects.Add(project with
            {
                Status = ResearchProjectStatuses.Normalize(project.Status),
                RecordingCount = recordingCatalog.Read(project.RecordingsDirectory).Count,
            });
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
            now,
            ResearchProjectStatuses.Normalize(draft.Status),
            draft.Notes.Trim());
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
        var selectedSessionId = SelectedRecording?.SessionId;
        Recordings.Clear();
        SelectedRecording = null;
        if (SelectedProject is null)
        {
            ApplyRecordingPage();
            return;
        }

        foreach (var recording in recordingCatalog.Read(SelectedProject.RecordingsDirectory))
        {
            Recordings.Add(new ProjectRecordingRow(
                recording.SessionId,
                ReadDisplayName(recording.RecordingDirectory, $"采集_{recording.RecordingStartUtc.ToLocalTime():yyyyMMdd_HHmmss}"),
                recording.RecordingStartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                $"{recording.SamplingRateHz} Hz",
                $"{recording.SignalChannelCount} 信号通道",
                recording.Status,
                recording.RecordingDirectory));
        }

        SelectedRecording = Recordings.FirstOrDefault(recording => recording.SessionId == selectedSessionId);
        recordingCurrentPage = 1;
        RaisePropertyChanged(nameof(RecordingCurrentPage));
        ApplyRecordingPage();
    }

    public async Task RenameRecordingAsync(ProjectRecordingRow recording, string name)
    {
        var displayName = name.Trim();
        if (string.IsNullOrWhiteSpace(displayName)) throw new InvalidOperationException("记录名称不能为空。");
        EnsureRecordingBelongsToSelectedProject(recording);
        var path = Path.Combine(recording.RecordingDirectory, "recording-display.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { displayName }));
        RefreshRecordings();
        notifications.PublishSuccess($"已将记录重命名为“{displayName}”。");
    }

    public async Task DeleteRecordingAsync(ProjectRecordingRow recording)
    {
        EnsureRecordingBelongsToSelectedProject(recording);
        var project = SelectedProject!;
        var trashRoot = Path.Combine(project.DirectoryPath, ".trash", "recordings");
        Directory.CreateDirectory(trashRoot);
        var destination = Path.Combine(trashRoot, $"{recording.SessionId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
        await Task.Run(() => Directory.Move(recording.RecordingDirectory, destination));
        await RefreshAsync();
        notifications.PublishSuccess($"已将数据记录“{recording.Name}”移至项目回收目录。原始文件尚可手动恢复。");
    }

    public void PreviousRecordingPage()
    {
        if (!CanGoToPreviousRecordingPage) return;
        recordingCurrentPage--;
        RaisePropertyChanged(nameof(RecordingCurrentPage));
        ApplyRecordingPage();
    }

    public void NextRecordingPage()
    {
        if (!CanGoToNextRecordingPage) return;
        recordingCurrentPage++;
        RaisePropertyChanged(nameof(RecordingCurrentPage));
        ApplyRecordingPage();
    }

    public void GoToRecordingPage(int page)
    {
        var normalized = Math.Clamp(page, 1, RecordingTotalPages);
        if (recordingCurrentPage == normalized) return;
        recordingCurrentPage = normalized;
        RaisePropertyChanged(nameof(RecordingCurrentPage));
        ApplyRecordingPage();
    }

    public void PreviousProjectPage()
    {
        if (!CanGoToPreviousProjectPage) return;
        projectCurrentPage--;
        RaisePropertyChanged(nameof(ProjectCurrentPage));
        ApplyProjectPage();
    }

    public void NextProjectPage()
    {
        if (!CanGoToNextProjectPage) return;
        projectCurrentPage++;
        RaisePropertyChanged(nameof(ProjectCurrentPage));
        ApplyProjectPage();
    }

    public void GoToProjectPage(int page)
    {
        var normalized = Math.Clamp(page, 1, ProjectTotalPages);
        if (projectCurrentPage == normalized) return;
        projectCurrentPage = normalized;
        RaisePropertyChanged(nameof(ProjectCurrentPage));
        ApplyProjectPage();
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
        var (from, to) = GetEffectiveCreatedDateRange();
        FilteredProjects.Clear();
        foreach (var project in Projects.Where(project =>
                     (query.Length == 0 ||
                      project.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                      project.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                      project.Number.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
                     (ProjectStatusFilter == ResearchProjectStatuses.All ||
                      project.NormalizedStatus == ProjectStatusFilter) &&
                     (!from.HasValue || project.CreatedAtUtc.ToLocalTime().Date >= from.Value) &&
                     (!to.HasValue || project.CreatedAtUtc.ToLocalTime().Date <= to.Value)))
        {
            FilteredProjects.Add(project);
        }

        projectCurrentPage = 1;
        RaisePropertyChanged(nameof(ProjectCurrentPage));
        ApplyProjectPage();
    }

    private void ApplyProjectPage()
    {
        var validPage = Math.Clamp(projectCurrentPage, 1, ProjectTotalPages);
        if (validPage != projectCurrentPage)
        {
            projectCurrentPage = validPage;
            RaisePropertyChanged(nameof(ProjectCurrentPage));
        }

        PagedProjects.Clear();
        foreach (var project in FilteredProjects.Skip((ProjectCurrentPage - 1) * ProjectPageSize).Take(ProjectPageSize))
        {
            PagedProjects.Add(project);
        }

        RaisePropertyChanged(nameof(ProjectTotalPages));
        RaisePropertyChanged(nameof(CanGoToPreviousProjectPage));
        RaisePropertyChanged(nameof(CanGoToNextProjectPage));
    }

    private void ApplyRecordingPage()
    {
        var validPage = Math.Clamp(recordingCurrentPage, 1, RecordingTotalPages);
        if (validPage != recordingCurrentPage)
        {
            recordingCurrentPage = validPage;
            RaisePropertyChanged(nameof(RecordingCurrentPage));
        }

        PagedRecordings.Clear();
        foreach (var recording in Recordings.Skip((RecordingCurrentPage - 1) * RecordingPageSize).Take(RecordingPageSize))
        {
            PagedRecordings.Add(recording);
        }

        RaisePropertyChanged(nameof(RecordingTotalPages));
        RaisePropertyChanged(nameof(CanGoToPreviousRecordingPage));
        RaisePropertyChanged(nameof(CanGoToNextRecordingPage));
    }

    private void EnsureRecordingBelongsToSelectedProject(ProjectRecordingRow recording)
    {
        var root = SelectedProject?.RecordingsDirectory ?? throw new InvalidOperationException("请先选择项目。");
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedDirectory = Path.GetFullPath(recording.RecordingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!normalizedDirectory.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(recording.RecordingDirectory))
            throw new InvalidOperationException("这条记录不属于当前项目，或记录目录已不存在。");
    }

    private static string ReadDisplayName(string directory, string fallback)
    {
        try
        {
            var path = Path.Combine(directory, "recording-display.json");
            if (!File.Exists(path)) return fallback;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("displayName", out var name) && !string.IsNullOrWhiteSpace(name.GetString())
                ? name.GetString()!
                : fallback;
        }
        catch (IOException) { return fallback; }
        catch (JsonException) { return fallback; }
    }

    private (DateTime? From, DateTime? To) GetEffectiveCreatedDateRange()
    {
        var today = DateTime.Today;
        return CreatedTimeFilter switch
        {
            "今天" => (today, today),
            "近7天" => (today.AddDays(-6), today),
            "近30天" => (today.AddDays(-29), today),
            "本月" => (new DateTime(today.Year, today.Month, 1), today),
            _ => (CreatedFrom?.Date, CreatedTo?.Date),
        };
    }
}
