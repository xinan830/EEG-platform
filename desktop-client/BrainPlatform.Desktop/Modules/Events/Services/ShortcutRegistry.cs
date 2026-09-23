namespace BrainPlatform.Desktop.Modules.Events.Services;

public sealed class ShortcutRegistry
{
    private readonly object sync = new();
    private readonly Dictionary<(ShortcutScope Scope, string Shortcut), string> registrations = new();

    public ShortcutRegistrationResult TryRegister(string shortcut, ShortcutScope scope, string commandId)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return ShortcutRegistrationResult.Success();
        var key = (scope, Normalize(shortcut));
        lock (sync)
        {
            if (registrations.TryGetValue(key, out var existing) && !string.Equals(existing, commandId, StringComparison.Ordinal))
                return ShortcutRegistrationResult.Failed(new ShortcutConflict(key.Item2, scope, existing));
            registrations[key] = commandId;
            return ShortcutRegistrationResult.Success();
        }
    }

    public void Unregister(string shortcut, ShortcutScope scope, string commandId)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return;
        lock (sync)
        {
            var key = (scope, Normalize(shortcut));
            if (registrations.TryGetValue(key, out var existing) && existing == commandId)
                registrations.Remove(key);
        }
    }

    public bool IsRegistered(string shortcut, ShortcutScope scope)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return false;
        lock (sync) return registrations.ContainsKey((scope, Normalize(shortcut)));
    }

    private static string Normalize(string shortcut) => string.Join('+', shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(part => part.ToUpperInvariant()));
}
