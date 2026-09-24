using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Events.Views;

internal static class EventShortcutInput
{
    public static EventDefinition? Match(
        IEnumerable<EventDefinition> definitions,
        ShortcutScope currentScope,
        KeyEventArgs e,
        out string shortcut)
    {
        shortcut = string.Empty;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.FocusedElement is TextBoxBase or ComboBox ||
            e.IsRepeat || key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or
                Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Tab or Key.None)
        {
            return null;
        }

        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("CTRL");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("ALT");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("SHIFT");
        parts.Add(key.ToString().ToUpperInvariant());
        shortcut = string.Join('+', parts);
        var normalizedShortcut = shortcut;

        return definitions.FirstOrDefault(item =>
            item.IsEnabled &&
            (item.ShortcutScope == currentScope || item.ShortcutScope == ShortcutScope.Global) &&
            !string.IsNullOrWhiteSpace(item.Shortcut) &&
            ShortcutRegistry.Normalize(item.Shortcut) == normalizedShortcut);
    }
}
