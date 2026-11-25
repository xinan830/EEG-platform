using BrainPlatform.Desktop.Modules.Projects.Api;

namespace BrainPlatform.Desktop.Modules.Projects.ViewModels;

public sealed partial class ProjectWorkspaceViewModel
{
    public async Task EditRecordingAsync(ProjectRecordingRow recording, string name, string subjectCode, string subjectName)
    {
        var displayName = name.Trim();
        if (string.IsNullOrEmpty(displayName) || displayName.Length > 4000)
            throw new InvalidOperationException("记录名称须为 1–4000 个字符。");
        if (subjectCode.Trim().Length > 160 || subjectName.Trim().Length > 160)
            throw new InvalidOperationException("受试者编号和姓名不能超过 160 个字符。");
        var project = SelectedProject ?? throw new InvalidOperationException("请先选择项目。");
        if (!Recordings.Contains(recording)) throw new InvalidOperationException("这条记录不属于当前项目。");
        if (recording.SourceKind == "backend")
        {
            var client = backendClient ?? throw new InvalidOperationException("项目服务尚未连接后端。");
            await client.EditSessionAsync(project.Id, recording.SessionId,
                new EditResearchSessionRequest(displayName, subjectCode.Trim(), subjectName.Trim()), CancellationToken.None);
        }
        else
        {
            EnsureRecordingBelongsToSelectedProject(recording);
            await RecordingMetadataStore.SaveAsync(recording.RecordingDirectory, displayName, new(subjectCode, subjectName));
            RefreshRecordings();
        }
        if (SelectedProject?.Id == project.Id) await RefreshImportedRecordingsAsync();
        notifications.PublishSuccess("记录信息已保存。");
    }
}
