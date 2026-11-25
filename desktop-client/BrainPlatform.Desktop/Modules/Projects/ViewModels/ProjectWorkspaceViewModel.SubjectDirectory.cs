using BrainPlatform.Desktop.Modules.Projects.Api;

namespace BrainPlatform.Desktop.Modules.Projects.ViewModels;

public sealed partial class ProjectWorkspaceViewModel
{
    private readonly SemaphoreSlim localSubjectSynchronization = new(1, 1);
    public string SubjectProjectId => SelectedProject?.Id ?? throw new InvalidOperationException("请先选择项目。");

    public async Task<RecordingSubject> ResolveSubjectAsync(string projectId, RecordingSubject subject, CancellationToken token = default)
    {
        RequireSubjectProject(projectId);
        subject.Validate();
        if (subject.SubjectId is { } id)
        {
            var subjects = await ListSubjectsAsync(token);
            RequireSubjectProject(projectId);
            var selected = subjects.FirstOrDefault(item => item.SubjectId == id)
                ?? throw new InvalidOperationException("受试者已被删除，请重新选择。");
            return ToRecordingSubject(selected) with { AgeAtRecordingYears = subject.AgeAtRecordingYears };
        }
        if (string.IsNullOrWhiteSpace(subject.Code) && string.IsNullOrWhiteSpace(subject.Name)) return new(AgeAtRecordingYears: subject.AgeAtRecordingYears);
        var client = backendClient ?? throw new InvalidOperationException("受试者服务尚未连接。");
        await client.EnsureProjectAsync(projectId, SelectedProject!.Name, SelectedProject.Description, token);
        var resolved = await client.ResolveSubjectAsync(projectId, new(subject.Code.Trim(), subject.Name.Trim(), subject.Sex), token);
        RequireSubjectProject(projectId);
        return resolved.Subject is { } value ? ToRecordingSubject(value) with { AgeAtRecordingYears = subject.AgeAtRecordingYears } : new(AgeAtRecordingYears: subject.AgeAtRecordingYears);
    }

    public async Task<RecordingSubject> UpdateSubjectAsync(string projectId, string id, string code, string name, CancellationToken token, string? sex = null)
    {
        RequireSubjectProject(projectId);
        var client = backendClient ?? throw new InvalidOperationException("受试者服务尚未连接。");
        await RefreshLocalSubjectsAsync(projectId, token);
        new RecordingSubject(code, name, id, sex).Validate();
        var updated = await client.UpdateSubjectAsync(projectId, id, new(code.Trim(), name.Trim(), sex), token);
        RequireSubjectProject(projectId);
        await RefreshImportedRecordingsAsync(token);
        return ToRecordingSubject(updated);
    }

    public async Task DeleteSubjectAsync(string projectId, string id, CancellationToken token)
    {
        RequireSubjectProject(projectId);
        var client = backendClient ?? throw new InvalidOperationException("受试者服务尚未连接。");
        await RefreshLocalSubjectsAsync(projectId, token);
        await client.DeleteSubjectAsync(projectId, id, token);
        RequireSubjectProject(projectId);
        await RefreshImportedRecordingsAsync(token);
    }

    private async Task<IReadOnlyList<BackendResearchSubject>> RefreshLocalSubjectsAsync(string projectId, CancellationToken token)
    {
        await localSubjectSynchronization.WaitAsync(token);
        try { return await SynchronizeLocalSubjectsAsync(projectId, token); }
        finally { localSubjectSynchronization.Release(); }
    }

    private async Task<IReadOnlyList<BackendResearchSubject>> SynchronizeLocalSubjectsAsync(string projectId, CancellationToken token)
    {
        var subjects = await backendClient!.ListSubjectsAsync(projectId, token);
        var directory = subjects.ToList();
        RequireSubjectProject(projectId);
        foreach (var row in Recordings.Where(item => item.SourceKind != "backend").ToArray())
        {
            token.ThrowIfCancellationRequested();
            RequireSubjectProject(projectId);
            var stored = RecordingMetadataStore.ReadSubject(row.RecordingDirectory);
            RecordingSubject resolved;
            if (stored.SubjectId is { } id)
                resolved = subjects.FirstOrDefault(item => item.SubjectId == id) is { } match
                    ? ToRecordingSubject(match) with { AgeAtRecordingYears = stored.AgeAtRecordingYears }
                    : new(AgeAtRecordingYears: stored.AgeAtRecordingYears);
            else
                resolved = await ResolveSubjectAsync(projectId, stored, token);
            if (stored != resolved)
                await RecordingMetadataStore.SaveAsync(row.RecordingDirectory, null, resolved, token);
            if (resolved.SubjectId is { } resolvedId && directory.All(item => item.SubjectId != resolvedId))
                directory.Add(new(resolvedId, projectId, resolved.Code, "", resolved.Name, resolved.Sex));
            RequireSubjectProject(projectId);
            ReplaceSubjectRow(row, row with { SubjectId = resolved.SubjectId, SubjectCode = resolved.Code, SubjectName = resolved.Name,
                SubjectSex = resolved.Sex, AgeAtRecordingYears = resolved.AgeAtRecordingYears });
        }
        return directory;
    }

    private void ReplaceSubjectRow(ProjectRecordingRow row, ProjectRecordingRow updated)
    {
        if (row == updated) return;
        var index = Recordings.IndexOf(row);
        if (index < 0) return;
        Recordings[index] = updated;
        var completed = CompletedRecordings.IndexOf(row);
        if (completed >= 0) CompletedRecordings[completed] = updated;
        if (SelectedRecording == row) SelectedRecording = updated;
    }

    private void RequireSubjectProject(string projectId)
    {
        if (SelectedProject?.Id != projectId) throw new InvalidOperationException("当前项目已改变，请重新打开受试者选择。");
    }

    public static RecordingSubject ToRecordingSubject(BackendResearchSubject subject) => new(subject.LocalCode ?? "", subject.Name, subject.SubjectId, subject.Sex);
}
