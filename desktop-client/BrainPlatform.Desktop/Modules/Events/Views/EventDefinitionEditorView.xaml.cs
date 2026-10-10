using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Events.Views;

public partial class EventDefinitionEditorView : UserControl
{
    public EventDefinitionEditorView() => InitializeComponent();


    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel workspace)
        {
            if (workspace.EventDefinitions.IsSaving) return;
            workspace.EventDefinitions.CancelCommand.Execute(null);
        }

        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowEventListView(refreshDefinitions: false);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace)
        {
            return;
        }

        try
        {
            await workspace.EventDefinitions.SaveDraftAsync();
            if (!workspace.EventDefinitions.IsEditing && Window.GetWindow(this) is MainWindow mainWindow)
            {
                mainWindow.ShowEventListView(refreshDefinitions: false);
            }
        }
        catch (Exception exception)
        {
            workspace.Notifications.PublishError(exception.Message);
        }
    }


    private void OnClearShortcutClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel workspace &&
            !workspace.EventDefinitions.IsDraftSystemDefinition)
        {
            workspace.EventDefinitions.DraftShortcut = string.Empty;
        }
    }

    private void OnShortcutPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (DataContext is not DesktopWorkspaceViewModel workspace ||
            e.IsRepeat || IsModifierKey(key) || key is Key.Tab or Key.Return or Key.Escape or Key.None)
        {
            return;
        }

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
