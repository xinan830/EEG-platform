using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Events.Views;

public partial class EventListView : UserControl
{
    public EventListView() => InitializeComponent();

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow) mainWindow.ShowSettingsView();
    }

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel workspace) workspace.EventDefinitions.BeginNew();
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace || sender is not FrameworkElement { DataContext: EventDefinition definition }) return;
        workspace.EventDefinitions.SelectedDefinition = definition;
        workspace.EventDefinitions.BeginEditSelected();
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace || sender is not FrameworkElement { DataContext: EventDefinition definition }) return;
        workspace.EventDefinitions.SelectedDefinition = definition;
        try { await workspace.EventDefinitions.DeleteSelectedAsync(); }
        catch (Exception exception) { workspace.Notifications.PublishError(exception.Message); }
    }

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel workspace && sender is FrameworkElement { Tag: string color })
        {
            workspace.EventDefinitions.SetDraftColor(color);
        }
    }

    private void OnShortcutPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace || IsModifierKey(e.Key))
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        workspace.EventDefinitions.DraftShortcut = FormatShortcut(key, Keyboard.Modifiers);
        e.Handled = true;
    }

    private static bool IsModifierKey(Key key) => key is Key.LeftAlt or Key.RightAlt or
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static string FormatShortcut(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join('+', parts);
    }
}
