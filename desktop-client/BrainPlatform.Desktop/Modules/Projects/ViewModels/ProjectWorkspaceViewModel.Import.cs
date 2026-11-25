using BrainPlatform.Desktop.Modules.Projects.Api;
using BrainPlatform.Desktop.Modules.Channels.Stores;
using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Projects.ViewModels;

public sealed partial class ProjectWorkspaceViewModel
{
    public async Task RefreshImportedRecordingsAsync(CancellationToken cancellationToken = default)
    {
        if (backendClient is null || SelectedProject is not { } project) return;
        try
        {
            var backendProject = await backendClient.EnsureProjectAsync(project.Id, project.Name, project.Description, cancellationToken);
            var sessions = await backendClient.ListSessionsAsync(backendProject.ProjectId, cancellationToken);
            if (SelectedProject?.Id != project.Id) return;
            await RefreshLocalSubjectsAsync(project.Id, cancellationToken);
            foreach (var session in sessions)
            {
                if (SelectedProject?.Id != project.Id) return;
                var existing = Recordings.FirstOrDefault(row => row.SourceKind == "backend" && row.SessionId == session.SessionId);
                if (existing is not null)
                {
                    ReplaceImportedRecording(existing, string.IsNullOrWhiteSpace(session.Label) ? existing.Name : session.Label,
                        session.SubjectCode ?? "", session.SubjectName, session.SubjectId, session.SubjectSex, session.AgeAtRecordingYears);
                    continue;
                }
                var recording = await backendClient.GetRecordingAsync(session.RecordingId, cancellationToken);
                if (SelectedProject?.Id != project.Id) return;
                if (recording.Extension is not (".edf" or ".bdf")) continue;
                var created = DateTimeOffset.TryParse(recording.CreatedAt, out var parsed) ? parsed : DateTimeOffset.MinValue;
                var row = new ProjectRecordingRow(session.SessionId,
                    string.IsNullOrWhiteSpace(session.Label) ? recording.OriginalName : session.Label,
                    created == DateTimeOffset.MinValue ? "未知" : created.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    TimeSpan.FromSeconds(recording.DurationSeconds ?? 0).ToString(@"hh\:mm\:ss"),
                    $"{recording.SamplingRateHz:0.##} Hz", $"{recording.Channels.Count} 信号通道", "已完成", string.Empty,
                    created == DateTimeOffset.MinValue ? null : created, recording.Id, "backend", recording.SourceSha256,
                    recording.Channels, (int)Math.Round(recording.SamplingRateHz ?? 0), recording.DurationSeconds ?? 0,
                    session.SubjectCode ?? "", session.SubjectName, session.SubjectId, session.SubjectSex, session.AgeAtRecordingYears);
                Recordings.Add(row);
                CompletedRecordings.Add(row);
            }
            ApplyRecordingPage();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { notifications.PublishError($"同步项目导入记录失败：{exception.Message}"); }
    }

    public async Task<BackendImportedRecording> ImportRecordingAsync(string filePath, string? subjectId,
        string? newSubjectLocalCode, IProgress<long>? progress, CancellationToken cancellationToken, string? newSubjectName = null,
        string? newSubjectSex = null, int? ageAtRecordingYears = null)
    {
        var project = SelectedProject ?? throw new InvalidOperationException("请先选择项目。");
        var client = backendClient ?? throw new InvalidOperationException("项目导入服务尚未连接后端。");
        var backendProject = await client.EnsureProjectAsync(project.Id, project.Name, project.Description, cancellationToken);
        var snapshot = new ActiveChannelConfigurationStore().TryLoadMostRecentlyApplied(out var profile)
            ? JsonSerializer.Serialize(profile)
            : null;
        var imported = await client.ImportRecordingAsync(backendProject.ProjectId, filePath, subjectId,
            newSubjectLocalCode, snapshot, progress, cancellationToken, newSubjectName, newSubjectSex, ageAtRecordingYears);
        await RefreshImportedRecordingsAsync(cancellationToken);
        SelectedRecording = Recordings.FirstOrDefault(row => row.RecordingId == imported.RecordingId);
        return imported;
    }

    private async Task RenameImportedRecordingAsync(ProjectRecordingRow recording, string displayName)
    {
        var project = SelectedProject ?? throw new InvalidOperationException("请先选择项目。");
        if (!Recordings.Contains(recording)) throw new InvalidOperationException("这条记录不属于当前项目。");
        var client = backendClient ?? throw new InvalidOperationException("项目服务尚未连接后端。");
        var session = await client.RenameSessionAsync(project.Id, recording.SessionId, displayName, CancellationToken.None);
        if (SelectedProject?.Id != project.Id) return;
        var current = Recordings.FirstOrDefault(row => row.SourceKind == "backend" && row.SessionId == session.SessionId);
        if (current is not null) ReplaceImportedRecording(current, session.Label);
        ApplyRecordingPage();
    }

    private void ReplaceImportedRecording(ProjectRecordingRow recording, string name, string? subjectCode = null, string? subjectName = null,
        string? subjectId = null, string? subjectSex = null, int? ageAtRecordingYears = null)
    {
        var renamed = recording with { Name = name, SubjectCode = subjectCode ?? recording.SubjectCode, SubjectName = subjectName ?? recording.SubjectName,
            SubjectId = subjectCode is null ? recording.SubjectId : subjectId,
            SubjectSex = subjectCode is null ? recording.SubjectSex : subjectSex,
            AgeAtRecordingYears = subjectCode is null ? recording.AgeAtRecordingYears : ageAtRecordingYears };
        if (recording == renamed) return;
        Recordings[Recordings.IndexOf(recording)] = renamed;
        var completedIndex = CompletedRecordings.IndexOf(recording);
        if (completedIndex >= 0) CompletedRecordings[completedIndex] = renamed;
        if (SelectedRecording == recording) SelectedRecording = renamed;
    }

    public async Task<IReadOnlyList<BackendResearchSubject>> ListSubjectsAsync(CancellationToken cancellationToken)
    {
        var project = SelectedProject ?? throw new InvalidOperationException("请先选择项目。");
        var client = backendClient ?? throw new InvalidOperationException("项目服务尚未连接后端。");
        await client.EnsureProjectAsync(project.Id, project.Name, project.Description, cancellationToken);
        RequireSubjectProject(project.Id);
        return await RefreshLocalSubjectsAsync(project.Id, cancellationToken);
    }
}
