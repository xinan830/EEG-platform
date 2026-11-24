namespace BrainPlatform.Desktop.Tests.Events;

public sealed class EventDefinitionEditorContractTests
{
    [Fact]
    public async Task New_editor_generates_a_stable_code_and_only_requires_a_name()
    {
        using var directory = new TestDirectory();
        var service = new EventDefinitionService(new EventDefinitionStore(directory.File("definitions.json")));
        var workspace = new EventDefinitionWorkspaceViewModel(service);
        workspace.BeginNew();

        var firstCode = workspace.DraftCode;
        Assert.StartsWith("EVT_", firstCode);
        Assert.Equal($"EVT_{workspace.Draft!.Id.ToUpperInvariant()}", firstCode);
        Assert.Equal(string.Empty, workspace.DraftName);
        Assert.False(workspace.CanSave);

        workspace.DraftName = "睁眼";
        Assert.True(workspace.CanSave);
        Assert.Equal(firstCode, workspace.DraftCode);

        await workspace.SaveDraftAsync();
        Assert.Equal(firstCode, Assert.Single(await service.ListAsync(CancellationToken.None)).Code);

        workspace.BeginNew();
        Assert.NotEqual(firstCode, workspace.DraftCode);
    }

    [Fact]
    public async Task Existing_code_cannot_change_but_user_facing_name_can()
    {
        using var directory = new TestDirectory();
        var service = new EventDefinitionService(new EventDefinitionStore(directory.File("definitions.json")));
        var definition = Definition("EO", "睁眼", null, ShortcutScope.Acquisition);
        await service.SaveAsync(definition, CancellationToken.None);
        var workspace = new EventDefinitionWorkspaceViewModel(service);
        await workspace.RefreshAsync();
        workspace.SelectedDefinition = Assert.Single(workspace.Definitions);
        workspace.BeginEditSelected();
        workspace.DraftName = "睁眼阶段";

        await workspace.SaveDraftAsync();

        var saved = Assert.Single(await service.ListAsync(CancellationToken.None));
        Assert.Equal("EO", saved.Code);
        Assert.Equal("睁眼阶段", saved.Name);

        var error = await Assert.ThrowsAsync<EventValidationException>(() =>
            service.SaveAsync(saved with { Code = "EC" }, CancellationToken.None));
        Assert.Equal("code_immutable", error.Code);
        Assert.Equal("EO", Assert.Single(await service.ListAsync(CancellationToken.None)).Code);
    }

    [Fact]
    public async Task Editor_disables_system_definition_and_clears_its_shortcut()
    {
        using var directory = new TestDirectory();
        var service = new EventDefinitionService(new EventDefinitionStore(directory.File("definitions.json")));
        var system = Definition("SYS_START", "开始", "F1", ShortcutScope.Acquisition, EventDefinitionSource.System);
        await service.SaveAsync(system, CancellationToken.None);
        var workspace = new EventDefinitionWorkspaceViewModel(service);
        await workspace.RefreshAsync();
        workspace.SelectedDefinition = Assert.Single(workspace.Definitions);
        workspace.BeginEditSelected();

        workspace.DraftIsEnabled = false;
        await workspace.SaveDraftAsync();

        var saved = Assert.Single(await service.ListAsync(CancellationToken.None));
        Assert.False(saved.IsEnabled);
        Assert.Null(saved.Shortcut);
    }

    [Fact]
    public async Task Description_limit_is_enforced_at_save_boundary()
    {
        using var directory = new TestDirectory();
        var service = new EventDefinitionService(new EventDefinitionStore(directory.File("definitions.json")));
        var definition = Definition("EO", "睁眼", null, ShortcutScope.Acquisition) with
        {
            Description = new string('x', 201),
        };

        var error = await Assert.ThrowsAsync<EventValidationException>(() =>
            service.SaveAsync(definition, CancellationToken.None));

        Assert.Equal("description_too_long", error.Code);
        Assert.Empty(await service.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Global_shortcut_conflicts_with_both_workspaces_but_local_scopes_can_reuse_a_key()
    {
        using var directory = new TestDirectory();
        var service = new EventDefinitionService(new EventDefinitionStore(directory.File("definitions.json")));
        await service.SaveAsync(Definition("GLOBAL", "全局", "F1", ShortcutScope.Global), CancellationToken.None);

        foreach (var scope in new[] { ShortcutScope.Acquisition, ShortcutScope.Review })
        {
            var error = await Assert.ThrowsAsync<EventValidationException>(() =>
                service.SaveAsync(Definition($"CONFLICT_{scope}", "冲突", "F1", scope), CancellationToken.None));
            Assert.Equal("shortcut_conflict", error.Code);
        }

        await service.SaveAsync(Definition("ACQ", "采集", "F2", ShortcutScope.Acquisition), CancellationToken.None);
        await service.SaveAsync(Definition("REVIEW", "回溯", "F2", ShortcutScope.Review), CancellationToken.None);
        Assert.Equal(3, (await service.ListAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Failed_save_does_not_keep_a_shortcut_registration()
    {
        using var directory = new TestDirectory();
        var blocker = directory.File("blocker");
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(blocker, "not a directory");
        var registry = new ShortcutRegistry();
        var service = new EventDefinitionService(new EventDefinitionStore(System.IO.Path.Combine(blocker, "definitions.json")), registry);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.SaveAsync(Definition("EO", "睁眼", "F1", ShortcutScope.Acquisition), CancellationToken.None));

        Assert.False(registry.IsRegistered("F1", ShortcutScope.Acquisition));
    }

    private static EventDefinition Definition(
        string code, string name, string? shortcut, ShortcutScope scope,
        EventDefinitionSource source = EventDefinitionSource.User) => new(
            Guid.NewGuid().ToString("N"), code, name, string.Empty, "#2563EB", true, shortcut,
            scope, source, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "brain-platform-editor-" + Guid.NewGuid().ToString("N"));

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
