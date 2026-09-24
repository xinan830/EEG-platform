using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Modules.Events.ViewModels;

public sealed class EventDefinitionWorkspaceViewModel : ObservableObject
{
    private readonly EventDefinitionService service;
    private readonly OperationNotificationCenter notifications;
    private readonly Func<string, CancellationToken, Task<bool>> referenceChecker;
    private readonly Func<CancellationToken, Task<IReadOnlySet<string>>> referenceScanner;
    private string searchText = string.Empty;
    private string sourceFilter = "全部";
    private string statusFilter = "全部";
    private EventDefinition? selectedDefinition;
    private EventDefinitionListRow? selectedRow;
    private int referenceScanVersion;
    private EventDefinition? draft;
    private bool isEditing;
    private string draftCode = string.Empty;
    private string draftName = string.Empty;
    private string draftDescription = string.Empty;
    private string draftColor = "#2563EB";
    private string draftShortcut = string.Empty;
    private ShortcutScope draftShortcutScope;
    private bool draftIsEnabled;
    private bool isSaving;
    private bool isCreating;
    private bool selectedDefinitionHasHistory;

    public EventDefinitionWorkspaceViewModel(
        EventDefinitionService? service = null,
        OperationNotificationCenter? notifications = null,
        Func<string, CancellationToken, Task<bool>>? referenceChecker = null,
        Func<CancellationToken, Task<IReadOnlySet<string>>>? referenceScanner = null)
    {
        this.service = service ?? new EventDefinitionService(new EventDefinitionStore());
        this.notifications = notifications ?? new OperationNotificationCenter();
        // The production workspace injects the project-wide checker and batch scanner.
        this.referenceChecker = referenceChecker ?? ((_, _) => Task.FromResult(false));
        this.referenceScanner = referenceScanner ?? ScanReferencesIndividuallyAsync;
        SaveCommand = new AsyncRelayCommand(SaveDraftAsync, ReportError);
        CancelCommand = new RelayCommand(CancelDraft);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, ReportError);
        DeleteCommand = new AsyncRelayCommand(DeleteSelectedAsync, ReportError);
    }

    public ObservableCollection<EventDefinition> Definitions { get; } = [];

    public ObservableCollection<EventDefinition> FilteredDefinitions { get; } = [];

    public ObservableCollection<EventDefinitionListRow> FilteredRows { get; } = [];

    private List<EventDefinitionListRow> rows = [];

    public IReadOnlyList<string> SourceFilterOptions { get; } = ["全部", "系统预设", "用户创建"];

    public IReadOnlyList<string> StatusFilterOptions { get; } = ["全部", "已启用", "已停用"];

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
            SelectedRow = FilteredRows.FirstOrDefault(row => row.Definition.Id == value?.Id);
            RaisePropertyChanged(nameof(CanDeleteSelected));
            RaisePropertyChanged(nameof(SelectedDeleteRestrictionText));
            _ = RefreshSelectedDeleteRestrictionAsync(value);
        }
    }

    public EventDefinitionListRow? SelectedRow
    {
        get => selectedRow;
        set
        {
            if (SetProperty(ref selectedRow, value)) SelectedDefinition = value?.Definition;
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

    public string DraftPageTitle => isCreating ? "新建事件" : "编辑事件";

    public string DraftSourceHelper => IsDraftSystemDefinition
        ? "该事件由系统预设。"
        : "用户事件的来源固定为用户创建。";

    public string DraftRestrictionText => IsDraftSystemDefinition
        ? "系统预设的快捷键和快捷键作用范围不可修改。"
        : string.Empty;

    public bool IsSaving
    {
        get => isSaving;
        private set
        {
            if (SetProperty(ref isSaving, value)) RaisePropertyChanged(nameof(CanSave));
        }
    }

    public bool CanSave => Draft is not null && !IsSaving && !string.IsNullOrWhiteSpace(DraftName);

    public int DraftDescriptionLength => DraftDescription.Length;

    public string DraftEnabledText => DraftIsEnabled ? "已启用" : "已停用";

    public Brush DraftColorPreviewBrush
    {
        get
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(DraftColor)!);
            }
            catch (FormatException)
            {
                return Brushes.Transparent;
            }
        }
    }

    public string DraftSourceText => IsDraftSystemDefinition ? "系统预设" : "用户创建";

    public string DraftCode
    {
        get => draftCode;
        private set => SetProperty(ref draftCode, value);
    }
    public string DraftName
    {
        get => draftName;
        set { if (SetProperty(ref draftName, value)) RaisePropertyChanged(nameof(CanSave)); }
    }
    public string DraftDescription
    {
        get => draftDescription;
        set
        {
            if (SetProperty(ref draftDescription, value)) RaisePropertyChanged(nameof(DraftDescriptionLength));
        }
    }
    public string DraftColor
    {
        get => draftColor;
        set
        {
            if (SetProperty(ref draftColor, value)) RaisePropertyChanged(nameof(DraftColorPreviewBrush));
        }
    }
    public string DraftShortcut { get => draftShortcut; set => SetProperty(ref draftShortcut, value); }
    public ShortcutScope DraftShortcutScope { get => draftShortcutScope; set => SetProperty(ref draftShortcutScope, value); }
    public bool DraftIsEnabled
    {
        get => draftIsEnabled;
        set
        {
            if (SetProperty(ref draftIsEnabled, value))
            {
                RaisePropertyChanged(nameof(DraftEnabledText));
                if (!value && IsDraftSystemDefinition) DraftShortcut = string.Empty;
            }
        }
    }

    public async Task RefreshAsync()
    {
        var scanVersion = ++referenceScanVersion;
        var selectedId = SelectedDefinition?.Id;
        var definitions = await service.ListAsync(CancellationToken.None);
        if (scanVersion != referenceScanVersion) return;
        SelectedDefinition = null;
        Definitions.Clear();
        foreach (var definition in definitions.OrderBy(item => item.Name, StringComparer.CurrentCulture))
            Definitions.Add(definition);
        rows = Definitions.Select(definition => new EventDefinitionListRow(definition)).ToList();
        ApplyFilter();
        SelectedDefinition = selectedId is null
            ? null
            : FilteredDefinitions.FirstOrDefault(item => item.Id == selectedId);

        try
        {
            var referencedIds = await referenceScanner(CancellationToken.None);
            if (scanVersion != referenceScanVersion) return;
            foreach (var row in rows)
                row.ReferenceStatus = referencedIds.Contains(row.Definition.Id)
                    ? EventReferenceStatus.Referenced : EventReferenceStatus.Unreferenced;
        }
        catch (Exception exception)
        {
            if (scanVersion != referenceScanVersion) return;
            foreach (var row in rows) row.ReferenceStatus = EventReferenceStatus.Unconfirmed;
            notifications.PublishError($"无法检查事件历史引用：{exception.Message}");
        }
    }

    private async Task<IReadOnlySet<string>> ScanReferencesIndividuallyAsync(CancellationToken cancellationToken)
    {
        var referencedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in Definitions)
        {
            if (await referenceChecker(definition.Id, cancellationToken)) referencedIds.Add(definition.Id);
        }
        return referencedIds;
    }

    public void BeginNew()
    {
        isCreating = true;
        RaisePropertyChanged(nameof(DraftPageTitle));
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N");
        Draft = new EventDefinition(
            id, $"EVT_{id.ToUpperInvariant()}", string.Empty, string.Empty,
            "#2563EB", true, null, ShortcutScope.Acquisition,
            EventDefinitionSource.User, 1, now, now);
        LoadDraftFields(Draft);
        IsEditing = true;
    }

    public void BeginEditSelected()
    {
        if (SelectedDefinition is null) return;
        isCreating = false;
        RaisePropertyChanged(nameof(DraftPageTitle));
        Draft = SelectedDefinition;
        LoadDraftFields(Draft);
        IsEditing = true;
    }

    public void SetDraft(EventDefinition value)
    {
        isCreating = !Definitions.Any(item => item.Id == value.Id);
        RaisePropertyChanged(nameof(DraftPageTitle));
        Draft = value;
        LoadDraftFields(value);
        IsEditing = true;
    }

    public async Task SaveDraftAsync()
    {
        if (Draft is null || IsSaving) return;
        IsSaving = true;
        try
        {
            var existing = Definitions.FirstOrDefault(item => item.Id == Draft.Id);
            var edited = Draft with
            {
                Code = Draft.Code,
                Name = DraftName.Trim(),
                Description = DraftDescription.Trim(),
                Color = DraftColor.Trim(),
                Shortcut = (!DraftIsEnabled && Draft.IsSystem) || string.IsNullOrWhiteSpace(DraftShortcut)
                    ? null : DraftShortcut.Trim(),
                ShortcutScope = DraftShortcutScope,
                IsEnabled = DraftIsEnabled,
            };
            if (existing?.IsSystem == true &&
                (!string.Equals(edited.Code, existing.Code, StringComparison.OrdinalIgnoreCase) ||
                 edited.Source != existing.Source ||
                 (edited.IsEnabled && !string.Equals(edited.Shortcut, existing.Shortcut, StringComparison.OrdinalIgnoreCase)) ||
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
        finally
        {
            IsSaving = false;
        }
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

    public Task<bool> IsDefinitionReferencedAsync(EventDefinition definition) =>
        referenceChecker(definition.Id, CancellationToken.None);

    public async Task SetEnabledAsync(EventDefinition definition, bool enabled)
    {
        var current = (await service.ListAsync(CancellationToken.None))
            .FirstOrDefault(item => item.Id == definition.Id)
            ?? throw new KeyNotFoundException($"未找到事件定义“{definition.Name}”。");
        if (current.IsEnabled == enabled) return;

        // Disabled system definitions cannot retain a shortcut in the event contract.
        var updated = current with
        {
            IsEnabled = enabled,
            Shortcut = !enabled && current.IsSystem ? null : current.Shortcut,
            Version = checked(current.Version + 1),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        await service.SaveAsync(updated, CancellationToken.None);
        await RefreshAsync();
        notifications.PublishSuccess(enabled ? "事件已启用。" : "事件已停用。");
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
        FilteredRows.Clear();
        foreach (var definition in FilteredDefinitions)
            FilteredRows.Add(rows.First(row => row.Definition.Id == definition.Id));
        SelectedRow = FilteredRows.FirstOrDefault(row => row.Definition.Id == SelectedDefinition?.Id);
        RaisePropertyChanged(nameof(CanDeleteSelected));
    }

    public void SetDraftColor(string color)
    {
        if (!string.IsNullOrWhiteSpace(color)) DraftColor = color.Trim();
    }

    private async Task RefreshSelectedDeleteRestrictionAsync(EventDefinition? definition)
    {
        selectedDefinitionHasHistory = definition is { IsSystem: false };
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
        RaisePropertyChanged(nameof(DraftSourceHelper));
        RaisePropertyChanged(nameof(DraftRestrictionText));
        RaisePropertyChanged(nameof(CanSave));
    }

    private void ReportError(Exception exception) => notifications.PublishError(exception.Message);
}
