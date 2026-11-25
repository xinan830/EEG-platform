using BrainPlatform.Desktop.Modules.Projects.Api;

namespace BrainPlatform.Desktop.Modules.Projects.ViewModels;

public sealed partial class ProjectWorkspaceViewModel
{
    public async Task EditRecordingAsync(ProjectRecordingRow recording, string name, RecordingSubject subject)
    {
        var displayName = name.Trim();
        if (string.IsNullOrEmpty(displayName) || displayName.Length > 4000)
            throw new InvalidOperationException("记录名称须为 1–4000 个字符。");
        var projectId = SubjectProjectId;
        recording = Recordings.FirstOrDefault(row => row.SessionId == recording.SessionId && row.SourceKind == recording.SourceKind)
            ?? throw new InvalidOperationException("这条记录不属于当前项目。");
        subject.Validate();
        var resolved = backendClient is null && recording.SourceKind != "backend" && subject.SubjectId is null
            ? subject with { Code = subject.Code.Trim(), Name = subject.Name.Trim() }
            : await ResolveSubjectAsync(projectId, subject);
        RequireSubjectProject(projectId);
        if (recording.SourceKind == "backend")
            await backendClient!.BindSessionSubjectAsync(projectId, recording.SessionId,
                new(displayName, resolved.SubjectId, resolved.AgeAtRecordingYears), CancellationToken.None);
        else
        {
            EnsureRecordingBelongsToSelectedProject(recording);
            await RecordingMetadataStore.SaveAsync(recording.RecordingDirectory, displayName, resolved);
            RefreshRecordings();
        }
        if (SelectedProject?.Id == projectId) await RefreshImportedRecordingsAsync();
        notifications.PublishSuccess("记录信息已保存。");
    }

    public Task EditRecordingAsync(ProjectRecordingRow recording, string name, string subjectCode, string subjectName) =>
        EditRecordingAsync(recording, name, new RecordingSubject(subjectCode, subjectName,
            Sex: (subjectCode.Trim(), subjectName.Trim()) == (recording.SubjectCode, recording.SubjectName)
                ? recording.SubjectSex : null, AgeAtRecordingYears: recording.AgeAtRecordingYears));
}
