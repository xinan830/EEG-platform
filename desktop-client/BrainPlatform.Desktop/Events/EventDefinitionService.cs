namespace BrainPlatform.Desktop.Events;

public sealed class EventDefinitionService
{
    private readonly EventDefinitionStore store;
    private readonly ShortcutRegistry shortcuts;

    public EventDefinitionService(EventDefinitionStore store, ShortcutRegistry? shortcuts = null)
    {
        this.store = store;
        this.shortcuts = shortcuts ?? new ShortcutRegistry();
    }

    public Task<IReadOnlyList<EventDefinition>> ListAsync(CancellationToken cancellationToken) => store.LoadAsync(cancellationToken);

    public async Task SaveAsync(EventDefinition definition, CancellationToken cancellationToken)
    {
        EventValidation.ValidateDefinition(definition);
        var existing = await store.LoadAsync(cancellationToken);
        if (existing.Any(item => item.Id != definition.Id && string.Equals(item.Code, definition.Code, StringComparison.OrdinalIgnoreCase)))
            throw new EventValidationException("duplicate_code", $"事件 Code“{definition.Code}”已存在。");

        var previous = existing.FirstOrDefault(item => item.Id == definition.Id);
        var registration = definition.IsEnabled && definition.Shortcut is not null
            ? shortcuts.TryRegister(definition.Shortcut, definition.ShortcutScope, DefinitionCommandId(definition.Id))
            : ShortcutRegistrationResult.Success();
        if (!registration.Succeeded)
            throw new EventValidationException("shortcut_conflict", $"快捷键“{registration.Conflict!.Shortcut}”已被命令“{registration.Conflict.ExistingCommand}”占用。");

        if (previous is not null && previous.Shortcut is not null &&
            (!definition.IsEnabled || !string.Equals(previous.Shortcut, definition.Shortcut, StringComparison.OrdinalIgnoreCase) || previous.ShortcutScope != definition.ShortcutScope))
            shortcuts.Unregister(previous.Shortcut, previous.ShortcutScope, DefinitionCommandId(previous.Id));

        await store.SaveAsync(definition, cancellationToken);
    }

    public async Task DisableAsync(string id, CancellationToken cancellationToken)
    {
        var definition = (await store.LoadAsync(cancellationToken)).FirstOrDefault(item => item.Id == id)
            ?? throw new KeyNotFoundException($"未找到事件定义“{id}”。");
        var disabled = definition with { IsEnabled = false, Shortcut = null, UpdatedAtUtc = DateTimeOffset.UtcNow, Version = checked(definition.Version + 1) };
        await SaveAsync(disabled, cancellationToken);
    }

    public async Task DeleteAsync(string id, bool isReferenced, CancellationToken cancellationToken)
    {
        var definition = (await store.LoadAsync(cancellationToken)).FirstOrDefault(item => item.Id == id)
            ?? throw new KeyNotFoundException($"未找到事件定义“{id}”。");
        if (definition.IsSystem || isReferenced)
            throw new EventValidationException("delete_restricted", "系统事件或已有历史记录引用的事件不能物理删除，应停用。");
        if (definition.Shortcut is not null)
            shortcuts.Unregister(definition.Shortcut, definition.ShortcutScope, DefinitionCommandId(definition.Id));
        await store.DeleteAsync(id, cancellationToken);
    }

    private static string DefinitionCommandId(string id) => $"event-definition:{id}";
}
