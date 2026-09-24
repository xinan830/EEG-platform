
namespace BrainPlatform.Desktop.Tests.Events;

public sealed class EventDefinitionWorkspaceViewModelTests
{
    [Fact]
    public async Task Filters_definitions_by_source_and_enabled_state()
    {
        using var temp = new TemporaryDirectory();
        var service = new EventDefinitionService(new EventDefinitionStore(Path.Combine(temp.Path, "definitions.json")));
        var userEnabled = Definition("EO", "睁眼", true, EventDefinitionSource.User);
        var userDisabled = Definition("EC", "闭眼", false, EventDefinitionSource.User);
        var systemEnabled = Definition("SYS_START", "系统开始", true, EventDefinitionSource.System);
        await service.SaveAsync(userEnabled, CancellationToken.None);
        await service.SaveAsync(userDisabled, CancellationToken.None);
        await service.SaveAsync(systemEnabled, CancellationToken.None);
        var workspace = new EventDefinitionWorkspaceViewModel(service);

        await workspace.RefreshAsync();
        workspace.SourceFilter = "用户创建";
        workspace.StatusFilter = "已启用";

        Assert.Equal([userEnabled.Id], workspace.FilteredDefinitions.Select(item => item.Id));
    }

    [Fact]
    public async Task Referenced_definition_is_exposed_as_non_deletable_to_the_view()
    {
        using var temp = new TemporaryDirectory();
        var service = new EventDefinitionService(new EventDefinitionStore(Path.Combine(temp.Path, "definitions.json")));
        var definition = Definition("EMG", "肌电", true, EventDefinitionSource.User);
        await service.SaveAsync(definition, CancellationToken.None);
        var workspace = new EventDefinitionWorkspaceViewModel(
            service,
            referenceChecker: (id, _) => Task.FromResult(id == definition.Id));

        await workspace.RefreshAsync();
        workspace.SelectedDefinition = Assert.Single(workspace.Definitions);

        Assert.False(workspace.CanDeleteSelected);
        Assert.Contains("历史记录引用", workspace.SelectedDeleteRestrictionText);
        Assert.True(await workspace.IsDefinitionReferencedAsync(definition));
    }

    [Fact]
    public async Task List_rows_show_persisted_reference_status_and_keep_it_when_filtered()
    {
        using var temp = new TemporaryDirectory();
        var service = new EventDefinitionService(new EventDefinitionStore(Path.Combine(temp.Path, "definitions.json")));
        var referenced = Definition("EO", "睁眼", true, EventDefinitionSource.User);
        var unreferenced = Definition("EC", "闭眼", false, EventDefinitionSource.User);
        await service.SaveAsync(referenced, CancellationToken.None);
        await service.SaveAsync(unreferenced, CancellationToken.None);
        var scanCount = 0;
        var workspace = new EventDefinitionWorkspaceViewModel(service,
            referenceScanner: _ =>
            {
                scanCount++;
                return Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { referenced.Id });
            });

        await workspace.RefreshAsync();

        Assert.Equal(1, scanCount);
        Assert.Equal("已被引用", workspace.FilteredRows.Single(row => row.Definition.Id == referenced.Id).ReferenceStatusText);
        Assert.False(workspace.FilteredRows.Single(row => row.Definition.Id == referenced.Id).CanDelete);
        Assert.Equal("未引用", workspace.FilteredRows.Single(row => row.Definition.Id == unreferenced.Id).ReferenceStatusText);
        Assert.True(workspace.FilteredRows.Single(row => row.Definition.Id == unreferenced.Id).CanDelete);

        workspace.StatusFilter = "已启用";
        Assert.Equal(referenced.Id, Assert.Single(workspace.FilteredRows).Definition.Id);
        workspace.SelectedRow = Assert.Single(workspace.FilteredRows);
        Assert.Equal(referenced.Id, workspace.SelectedDefinition?.Id);
    }

    [Fact]
    public async Task Failed_reference_scan_never_displays_unreferenced_or_enables_delete()
    {
        using var temp = new TemporaryDirectory();
        var service = new EventDefinitionService(new EventDefinitionStore(Path.Combine(temp.Path, "definitions.json")));
        await service.SaveAsync(Definition("EO", "睁眼", true, EventDefinitionSource.User), CancellationToken.None);
        var workspace = new EventDefinitionWorkspaceViewModel(service,
            referenceScanner: _ => throw new IOException("recording index unavailable"));

        await workspace.RefreshAsync();

        var row = Assert.Single(workspace.FilteredRows);
        Assert.Equal("未确认", row.ReferenceStatusText);
        Assert.False(row.CanDelete);
    }

    [Fact]
    public async Task Reference_scan_keeps_rows_visible_and_non_deletable_while_pending()
    {
        using var temp = new TemporaryDirectory();
        var service = new EventDefinitionService(new EventDefinitionStore(Path.Combine(temp.Path, "definitions.json")));
        var definition = Definition("EO", "睁眼", true, EventDefinitionSource.User);
        await service.SaveAsync(definition, CancellationToken.None);
        var scanStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishScan = new TaskCompletionSource<IReadOnlySet<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workspace = new EventDefinitionWorkspaceViewModel(service,
            referenceScanner: _ =>
            {
                scanStarted.SetResult();
                return finishScan.Task;
            });

        var refresh = workspace.RefreshAsync();
        await scanStarted.Task;
        var row = Assert.Single(workspace.FilteredRows);
        Assert.Equal("检查中", row.ReferenceStatusText);
        Assert.False(row.CanDelete);

        finishScan.SetResult(new HashSet<string> { definition.Id });
        await refresh;
        Assert.Equal("已被引用", row.ReferenceStatusText);
    }

    [Fact]
    public async Task Reference_scan_includes_active_and_trashed_recordings()
    {
        using var temp = new TemporaryDirectory();
        var projectDirectory = Path.Combine(temp.Path, "project");
        var projectStore = new ResearchProjectStore(Path.Combine(temp.Path, "projects.json"));
        var now = DateTimeOffset.UtcNow;
        await projectStore.SaveAsync(new ResearchProject(
            "project", "P001", "项目", "", "", [], "tester", projectDirectory, now, now),
            CancellationToken.None);

        async Task SaveEventAsync(string directory, string definitionId)
        {
            await new RecordingEventStore(directory).UpsertAsync(new RecordingEvent(
                Guid.NewGuid().ToString("N"), "recording", definitionId,
                new EventDefinitionSnapshot("EO", "睁眼", "#2563EB", 1),
                EventSource.ManualButton, null, null, 0, 0, now, now), CancellationToken.None);
        }

        await SaveEventAsync(Path.Combine(projectDirectory, "recordings", "active"), "active-definition");
        await SaveEventAsync(Path.Combine(projectDirectory, ".trash", "recordings", "deleted"), "trashed-definition");

        var referencedIds = await DesktopWorkspaceViewModel.LoadReferencedEventDefinitionIdsAsync(
            projectStore, CancellationToken.None);

        Assert.Contains("active-definition", referencedIds);
        Assert.Contains("trashed-definition", referencedIds);
    }

    [Fact]
    public async Task Refresh_preserves_the_visible_selected_definition()
    {
        using var temp = new TemporaryDirectory();
        var service = new EventDefinitionService(new EventDefinitionStore(Path.Combine(temp.Path, "definitions.json")));
        var definition = Definition("EO", "睁眼", true, EventDefinitionSource.User);
        await service.SaveAsync(definition, CancellationToken.None);
        var workspace = new EventDefinitionWorkspaceViewModel(service);
        await workspace.RefreshAsync();
        workspace.SelectedDefinition = Assert.Single(workspace.FilteredDefinitions);

        await workspace.RefreshAsync();

        Assert.Equal(definition.Id, workspace.SelectedDefinition?.Id);
        Assert.Same(workspace.SelectedDefinition, Assert.Single(workspace.FilteredDefinitions));
    }

    [Fact]
    public async Task Row_toggle_persists_status_and_updates_filtered_count()
    {
        using var temp = new TemporaryDirectory();
        var service = new EventDefinitionService(new EventDefinitionStore(Path.Combine(temp.Path, "definitions.json")));
        var definition = Definition("EO", "睁眼", true, EventDefinitionSource.User);
        await service.SaveAsync(definition, CancellationToken.None);
        var workspace = new EventDefinitionWorkspaceViewModel(service);
        await workspace.RefreshAsync();
        workspace.StatusFilter = "已启用";

        await workspace.SetEnabledAsync(definition, false);

        Assert.Empty(workspace.FilteredDefinitions);
        var persisted = Assert.Single(await service.ListAsync(CancellationToken.None));
        Assert.False(persisted.IsEnabled);
        Assert.Equal(2, persisted.Version);

        await workspace.SetEnabledAsync(persisted, true);
        Assert.Single(workspace.FilteredDefinitions);
        Assert.True(Assert.Single(await service.ListAsync(CancellationToken.None)).IsEnabled);
    }

    private static EventDefinition Definition(string code, string name, bool enabled, EventDefinitionSource source) => new(
        Guid.NewGuid().ToString("N"), code, name, string.Empty, "#2563EB", enabled, null,
        ShortcutScope.Acquisition, source, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "brain-platform-event-vm-" + Guid.NewGuid().ToString("N"));
        public string Path { get; } = string.Empty;
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
