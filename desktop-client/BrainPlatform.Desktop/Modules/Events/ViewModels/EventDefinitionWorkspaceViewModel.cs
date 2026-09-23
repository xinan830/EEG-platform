using System.Collections.ObjectModel;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Events.ViewModels;

public sealed class EventDefinitionWorkspaceViewModel : ObservableObject
{
    private readonly EventDefinitionService service;
    private readonly OperationNotificationCenter notifications;
    private readonly Func<string, CancellationToken, Task<bool>> referenceChecker;
    private string searchText = string.Empty;
    private string sourceFilter = "全部来源";
    private string statusFilter = "全部状态";
    private EventDefinition? selectedDefinition;
    private EventDefinition? draft;
    private bool isEditing;
    private string draftCode = string.Empty;
    private string draftName = string.Empty;
    private string draftDescription = string.Empty;
    private string draftColor = "#2563EB";
    private string draftShortcut = string.Empty;
    private ShortcutScope draftShortcutScope;
    private bool draftIsEnabled;
    private bool selectedDefinitionHasHistory;

    public EventDefinitionWorkspaceViewModel(
        EventDefinitionService? service = null,
        OperationNotificationCenter? notifications = null,
        Func<string, CancellationToken, Task<bool>>? referenceChecker = null)
    {
        this.service = service ?? new EventDefinitionService(new EventDefinitionStore());
        this.notifications = notifications ?? new OperationNotificationCenter();
        // The workspace has no project-wide recording index yet. Callers that
        // own that index inject a real checker; the local settings page treats
        // definitions as unreferenced until such an index is available.
        this.referenceChecker = referenceChecker ?? ((_, _) => Task.FromResult(false));
        SaveCommand = new AsyncRelayCommand(SaveDraftAsync, ReportError);
        CancelCommand = new RelayCommand(CancelDraft);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, ReportError);
        DeleteCommand = new AsyncRelayCommand(DeleteSelectedAsync, ReportError);
    }

    public ObservableCollection<EventDefinition> Definitions { get; } = [];

    public ObservableCollection<EventDefinition> FilteredDefinitions { get; } = [];

    public IReadOnlyList<string> SourceFilterOptions { get; } = ["全部来源", "系统预设", "用户创建"];

    public IReadOnlyList<string> StatusFilterOptions { get; } = ["全部状态", "已启用", "已停用"];

    public IReadOnlyList<ShortcutScope> ShortcutScopes { get; } = [
        ShortcutScope.Acquisition,
        ShortcutScope.Review,
        ShortcutScope.Global,
    ];

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand DeleteCommand { get; }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value)) ApplyFilter();
        }
    }

    public string SourceFilter
    {
        get => sourceFilter;
        set
        {
            if (SetProperty(ref sourceFilter, value)) ApplyFilter();
        }
    }

    public string StatusFilter
    {
        get => statusFilter;
        set
        {
            if (SetProperty(ref statusFilter, value)) ApplyFilter();
        }
    }

    public EventDefinition? SelectedDefinition
    {
        get => selectedDefinition;
        set
        {
            if (!SetProperty(ref selectedDefinition, value)) return;
            RaisePropertyChanged(nameof(CanDeleteSelected));
            RaisePropertyChanged(nameof(SelectedDeleteRestrictionText));
            _ = RefreshSelectedDeleteRestrictionAsync(value);
        }
    }

    public EventDefinition? Draft
    {
        get => draft;
        private set => SetProperty(ref draft, value);
    }

    public bool IsEditing
    {
        get => isEditing;
        private set => SetProperty(ref isEditing, value);
    }

    public bool CanDeleteSelected => SelectedDefinition is { IsSystem: false } && !selectedDefinitionHasHistory;

    public string SelectedDeleteRestrictionText => SelectedDefinition switch
    {
        null => string.Empty,
        { IsSystem: true } => "系统预设不可删除。",
        _ when selectedDefinitionHasHistory => "该事件已被历史记录引用，只能停用，不能删除。",
        _ => string.Empty,
    };

    public bool IsDraftSystemDefinition => Draft?.IsSystem == true;

    public string DraftSourceText => IsDraftSystemDefinition ? "系统预设" : "用户创建";

    public string DraftCode { get => draftCode; set => SetProperty(ref draftCode, value); }
    public string DraftName { get => draftName; set => SetProperty(ref draftName, value); }
    public string DraftDescription { get => draftDescription; set => SetProperty(ref draftDescription, value); }
    public string DraftColor { get => draftColor; set => SetProperty(ref draftColor, value); }
    public string DraftShortcut { get => draftShortcut; set => SetProperty(ref draftShortcut, value); }
    public ShortcutScope DraftShortcutScope { get => draftShortcutScope; set => SetProperty(ref draftShortcutScope, value); }
    public bool DraftIsEnabled { get => draftIsEnabled; set => SetProperty(ref draftIsEnabled, value); }

    public async Task RefreshAsync()
    {
        var definitions = await service.ListAsync(CancellationToken.None);
        Definitions.Clear();
        foreach (var definition in definitions.OrderBy(item => item.Name, StringComparer.CurrentCulture))
            Definitions.Add(definition);
        ApplyFilter();
        if (SelectedDefinition is { } selected)
            SelectedDefinition = Definitions.FirstOrDefault(item => item.Id == selected.Id);
        await RefreshSelectedDeleteRestrictionAsync(SelectedDefinition);
    }

    public void BeginNew()
    {
        var now = DateTimeOffset.UtcNow;
        Draft = new EventDefinition(
            Guid.NewGuid().ToString("N"), "NEW_EVENT", "新事件", string.Empty,
            "#2563EB", true, null, ShortcutScope.Acquisition,
            EventDefinitionSource.User, 1, now, now);
        LoadDraftFields(Draft);
        IsEditing = true;
    }

    public void BeginEditSelected()
    {
        if (SelectedDefinition is null) return;
        Draft = SelectedDefinition;
        LoadDraftFields(Draft);
        IsEditing = true;
    }

    public void SetDraft(EventDefinition value)
    {
        Draft = value;
        LoadDraftFields(value);
        IsEditing = true;
    }

    private async Task SaveDraftAsync()
    {
        if (Draft is null) return;
        var existing = Definitions.FirstOrDefault(item => item.Id == Draft.Id);
        var edited = Draft with
        {
            Code = DraftCode.Trim(),
            Name = DraftName.Trim(),
            Description = DraftDescription.Trim(),
            Color = DraftColor.Trim(),
            Shortcut = string.IsNullOrWhiteSpace(DraftShortcut) ? null : DraftShortcut.Trim(),
            ShortcutScope = DraftShortcutScope,
            IsEnabled = DraftIsEnabled,
        };
        if (existing?.IsSystem == true &&
            (!string.Equals(edited.Code, existing.Code, StringComparison.OrdinalIgnoreCase) ||
             edited.Source != existing.Source ||
             !string.Equals(edited.Shortcut, existing.Shortcut, StringComparison.OrdinalIgnoreCase) ||
             edited.ShortcutScope != existing.ShortcutScope))
        {
            throw new EventValidationException("system_definition_restricted", "系统事件只能修改显示名称、说明、颜色和启用状态。");
        }
        var next = existing is null
            ? edited
            : edited with { Version = edited.Version <= existing.Version ? checked(existing.Version + 1) : edited.Version, UpdatedAtUtc = DateTimeOffset.UtcNow };
        await service.SaveAsync(next, CancellationToken.None);
        Draft = null;
        IsEditing = false;
        RaiseDraftPropertiesChanged();
        await RefreshAsync();
        SelectedDefinition = Definitions.FirstOrDefault(item => item.Id == next.Id);
        notifications.PublishSuccess("事件定义已保存。");
    }

    private void CancelDraft()
    {
        Draft = null;
        IsEditing = false;
        RaiseDraftPropertiesChanged();
    }

    private void LoadDraftFields(EventDefinition value)
    {
        DraftCode = value.Code;
        DraftName = value.Name;
        DraftDescription = value.Description;
        DraftColor = value.Color;
        DraftShortcut = value.Shortcut ?? string.Empty;
        DraftShortcutScope = value.ShortcutScope;
        DraftIsEnabled = value.IsEnabled;
        RaiseDraftPropertiesChanged();
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedDefinition is not { } selected) return;
        var isReferenced = await referenceChecker(selected.Id, CancellationToken.None);
        await service.DeleteAsync(selected.Id, isReferenced, CancellationToken.None);
        SelectedDefinition = null;
        await RefreshAsync();
        notifications.PublishSuccess("事件定义已删除。");
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        IEnumerable<EventDefinition> values = string.IsNullOrEmpty(query)
            ? Definitions
            : Definitions.Where(item => item.Code.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                        item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                        item.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        values = SourceFilter switch
        {
            "系统预设" => values.Where(item => item.IsSystem),
            "用户创建" => values.Where(item => !item.IsSystem),
            _ => values,
        };
        values = StatusFilter switch
        {
            "已启用" => values.Where(item => item.IsEnabled),
            "已停用" => values.Where(item => !item.IsEnabled),
            _ => values,
        };
        FilteredDefinitions.Clear();
        foreach (var value in values) FilteredDefinitions.Add(value);
        RaisePropertyChanged(nameof(CanDeleteSelected));
    }

    public void SetDraftColor(string color)
    {
        if (!string.IsNullOrWhiteSpace(color)) DraftColor = color.Trim();
    }

    private async Task RefreshSelectedDeleteRestrictionAsync(EventDefinition? definition)
    {
        selectedDefinitionHasHistory = false;
        RaisePropertyChanged(nameof(CanDeleteSelected));
        RaisePropertyChanged(nameof(SelectedDeleteRestrictionText));
        if (definition is null || definition.IsSystem) return;

        try
        {
            var isReferenced = await referenceChecker(definition.Id, CancellationToken.None);
            if (SelectedDefinition?.Id != definition.Id) return;
            selectedDefinitionHasHistory = isReferenced;
            RaisePropertyChanged(nameof(CanDeleteSelected));
            RaisePropertyChanged(nameof(SelectedDeleteRestrictionText));
        }
        catch (Exception exception)
        {
            notifications.PublishError($"无法检查事件历史引用：{exception.Message}");
        }
    }

    private void RaiseDraftPropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsDraftSystemDefinition));
        RaisePropertyChanged(nameof(DraftSourceText));
    }

    private void ReportError(Exception exception) => notifications.PublishError(exception.Message);
}
