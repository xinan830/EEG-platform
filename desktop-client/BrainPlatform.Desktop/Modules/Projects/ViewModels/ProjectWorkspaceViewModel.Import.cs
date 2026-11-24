using BrainPlatform.Desktop.Modules.Projects.Api;

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
            foreach (var session in sessions)
            {
                if (SelectedProject?.Id != project.Id) return;
                if (Recordings.Any(row => row.SessionId == session.SessionId)) continue;
                var recording = await backendClient.GetRecordingAsync(session.RecordingId, cancellationToken);
                if (SelectedProject?.Id != project.Id) return;
                if (recording.Extension is not (".edf" or ".bdf")) continue;
                var created = DateTimeOffset.TryParse(recording.CreatedAt, out var parsed) ? parsed : DateTimeOffset.MinValue;
                var row = new ProjectRecordingRow(session.SessionId, recording.OriginalName,
                    created == DateTimeOffset.MinValue ? "未知" : created.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    TimeSpan.FromSeconds(recording.DurationSeconds ?? 0).ToString(@"hh\:mm\:ss"),
                    $"{recording.SamplingRateHz:0.##} Hz", $"{recording.Channels.Count} 信号通道", "已完成", string.Empty,
                    created == DateTimeOffset.MinValue ? null : created, recording.Id, "backend", recording.SourceSha256,
                    recording.Channels, (int)Math.Round(recording.SamplingRateHz ?? 0), recording.DurationSeconds ?? 0);
                Recordings.Add(row);
                CompletedRecordings.Add(row);
            }
            ApplyRecordingPage();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { notifications.PublishError($"同步项目导入记录失败：{exception.Message}"); }
    }

    public async Task<BackendImportedRecording> ImportRecordingAsync(string filePath, string? subjectId,
        string? newSubjectLocalCode, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var project = SelectedProject ?? throw new InvalidOperationException("请先选择项目。");
        var client = backendClient ?? throw new InvalidOperationException("项目导入服务尚未连接后端。");
        var backendProject = await client.EnsureProjectAsync(project.Id, project.Name, project.Description, cancellationToken);
        var imported = await client.ImportRecordingAsync(backendProject.ProjectId, filePath, subjectId,
            newSubjectLocalCode, progress, cancellationToken);
        await RefreshImportedRecordingsAsync(cancellationToken);
        SelectedRecording = Recordings.FirstOrDefault(row => row.RecordingId == imported.RecordingId);
        return imported;
    }

    public async Task<IReadOnlyList<BackendResearchSubject>> ListSubjectsAsync(CancellationToken cancellationToken)
    {
        var project = SelectedProject ?? throw new InvalidOperationException("请先选择项目。");
        var client = backendClient ?? throw new InvalidOperationException("项目服务尚未连接后端。");
        var resolved = await client.EnsureProjectAsync(project.Id, project.Name, project.Description, cancellationToken);
        return await client.ListSubjectsAsync(resolved.ProjectId, cancellationToken);
    }
}
