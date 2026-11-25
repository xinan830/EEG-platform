using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Modules.Projects.Api;

namespace BrainPlatform.Desktop.Modules.Projects.ViewModels;

/// <summary>One project directory and identity selection policy for import, acquisition and editing.</summary>
public sealed class SubjectSelectorViewModel : ObservableObject
{
    private ProjectWorkspaceViewModel? workspace;
    private string projectId = "";
    private BackendResearchSubject? selected;
    private string code = "", name = "", error = "";
    private string sex = "", ageText = "";
    public IReadOnlyList<KeyValuePair<string, string>> SexOptions { get; } =
        [new("", "未填写"), new("male", "男"), new("female", "女"), new("other", "其他")];
    private bool busy, editing;
    private bool initialized;
    private int initializationVersion;
    private RecordingSubject initialSelection = new();
    public SubjectSelectorViewModel() => Reload([]);
    public ObservableCollection<BackendResearchSubject> Subjects { get; } = [];
    public BackendResearchSubject? Selected
    {
        get => selected;
        set
        {
            if (!SetProperty(ref selected, value)) return;
            IsEditing = false;
            Code = value?.SubjectId is { Length: > 0 } ? value.LocalCode ?? "" : "";
            Name = value?.SubjectId is { Length: > 0 } ? value.Name : "";
            Sex = value?.Sex ?? "";
            Error = "";
            RaisePropertyChanged(nameof(IsNew));
            RaisePropertyChanged(nameof(CanManage));
            RaisePropertyChanged(nameof(ShowFields));
            RaisePropertyChanged(nameof(ShowSex));
        }
    }
    public string Code { get => code; set => SetProperty(ref code, value); }
    public string Name { get => name; set => SetProperty(ref name, value); }
    public string Sex { get => sex; set => SetProperty(ref sex, value); }
    public string AgeText { get => ageText; set => SetProperty(ref ageText, value); }
    public bool ShowSex => IsNew || CanManage;
    public string Error { get => error; set => SetProperty(ref error, value); }
    public bool IsBusy { get => busy; set { SetProperty(ref busy, value); RaisePropertyChanged(nameof(IsAvailable)); } }
    public bool IsAvailable => !IsBusy;
    public bool IsNew => Selected?.SubjectId == "";
    public bool CanManage => Selected?.SubjectId is { Length: > 0 } id && id != "none";
    public bool ShowFields => IsNew || IsEditing;
    public bool IsEditing
    {
        get => editing;
        private set { SetProperty(ref editing, value); RaisePropertyChanged(nameof(ShowFields)); RaisePropertyChanged(nameof(CanSelect)); }
    }
    public bool CanSelect => !IsEditing;

    public async Task InitializeAsync(ProjectWorkspaceViewModel owner, RecordingSubject initial, CancellationToken token,
        ProjectRecordingRow? recording = null)
    {
        var version = ++initializationVersion;
        initialized = false;
        initialSelection = initial;
        workspace = owner;
        projectId = owner.SubjectProjectId;
        IsBusy = true;
        try
        {
            var subjects = await owner.ListSubjectsAsync(token);
            token.ThrowIfCancellationRequested();
            if (version != initializationVersion) return;
            CheckProject();
            if (recording is not null)
            {
                var current = owner.Recordings.FirstOrDefault(row => row.SessionId == recording.SessionId && row.SourceKind == recording.SourceKind)
                    ?? throw new InvalidOperationException("记录已被移除，请重新打开编辑。");
                initial = new(current.SubjectCode, current.SubjectName, current.SubjectId, current.SubjectSex, current.AgeAtRecordingYears);
                initialSelection = initial;
            }
            Reload(subjects);
            if (initial.SubjectId is { } id)
                Selected = Subjects.FirstOrDefault(item => item.SubjectId == id) ?? Subjects[0];
            else if (!string.IsNullOrWhiteSpace(initial.Code) || !string.IsNullOrWhiteSpace(initial.Name))
            {
                Selected = Subjects[1];
                Code = initial.Code;
                Name = initial.Name;
                Sex = initial.Sex ?? "";
            }
            AgeText = initial.AgeAtRecordingYears?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
            initialized = true;
        }
        finally { if (version == initializationVersion) IsBusy = false; }
    }

    public RecordingSubject Selection
    {
        get
        {
            if (IsBusy || IsEditing) throw new InvalidOperationException("请先完成受试者信息编辑。");
            if (!initialized) return initialSelection;
            var subject = IsNew ? new RecordingSubject(Code.Trim(), Name.Trim(), Sex: string.IsNullOrEmpty(Sex) ? null : Sex)
                : CanManage ? ProjectWorkspaceViewModel.ToRecordingSubject(Selected!) : new();
            int? age = null;
            if (!string.IsNullOrWhiteSpace(AgeText))
            {
                if (!int.TryParse(AgeText.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
                    throw new InvalidOperationException("采集时年龄须为 0–150 之间的整数，或留空。");
                age = value;
            }
            subject = subject with { AgeAtRecordingYears = age };
            subject.Validate();
            return subject;
        }
    }

    public async Task<RecordingSubject> ResolveAsync(CancellationToken token)
    {
        CheckProject();
        var selection = Selection;
        IsBusy = true;
        try
        {
            var value = await workspace!.ResolveSubjectAsync(projectId, selection, token);
            token.ThrowIfCancellationRequested();
            CheckProject();
            if (value.SubjectId is not null)
            {
                SelectSavedSubject(value);
            }
            return value;
        }
        finally { IsBusy = false; }
    }

    public void BeginEdit() { if (!IsBusy && CanManage) IsEditing = true; }
    public void CancelEdit()
    {
        Code = Selected?.LocalCode ?? "";
        Name = Selected?.Name ?? "";
        Sex = Selected?.Sex ?? "";
        IsEditing = false;
        Error = "";
    }

    public async Task SaveEditAsync(CancellationToken token)
    {
        CheckProject();
        if (IsBusy || !CanManage || !IsEditing) return;
        var id = Selected!.SubjectId;
        IsBusy = true;
        try
        {
            var saved = await workspace!.UpdateSubjectAsync(projectId, id, Code, Name, token, string.IsNullOrEmpty(Sex) ? null : Sex);
            token.ThrowIfCancellationRequested();
            CheckProject();
            SelectSavedSubject(saved);
            IsEditing = false;
        }
        finally { IsBusy = false; }
    }

    public async Task DeleteAsync(CancellationToken token)
    {
        CheckProject();
        if (IsBusy || !CanManage) return;
        var id = Selected!.SubjectId;
        IsBusy = true;
        try
        {
            await workspace!.DeleteSubjectAsync(projectId, id, token);
            token.ThrowIfCancellationRequested();
            CheckProject();
            Subjects.Remove(Subjects.First(item => item.SubjectId == id));
            Selected = Subjects[0];
        }
        finally { IsBusy = false; }
    }

    private void Reload(IReadOnlyList<BackendResearchSubject> subjects)
    {
        Subjects.Clear();
        Subjects.Add(new("none", projectId, "不关联受试者", ""));
        Subjects.Add(new("", projectId, "新建受试者…", ""));
        foreach (var item in subjects) Subjects.Add(item);
        Selected = Subjects[0];
    }

    private void SelectSavedSubject(RecordingSubject subject)
    {
        var saved = new BackendResearchSubject(subject.SubjectId!, projectId, subject.Code, "", subject.Name, subject.Sex);
        var existing = Subjects.FirstOrDefault(item => item.SubjectId == subject.SubjectId);
        if (existing is null) Subjects.Add(saved);
        else Subjects[Subjects.IndexOf(existing)] = saved;
        Selected = saved;
    }

    private void CheckProject()
    {
        if (workspace is null || workspace.SubjectProjectId != projectId)
            throw new InvalidOperationException("当前项目已改变，请重新打开受试者选择。");
    }
}
