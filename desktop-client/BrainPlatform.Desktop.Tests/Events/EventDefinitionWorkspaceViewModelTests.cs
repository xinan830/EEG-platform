using BrainPlatform.Desktop.Events;
using BrainPlatform.Desktop.ViewModels;

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
