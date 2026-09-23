using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BrainPlatform.Desktop.Events;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Views;

public partial class AcquisitionWorkspaceView : UserControl
{
    public AcquisitionWorkspaceView()
    {
        InitializeComponent();
    }

    private async void OnFinishRecordingClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace)
        {
            return;
        }

        var sessionWindow = Window.GetWindow(this) as EegSessionWindow;
        try
        {
            await workspace.Acquisition.FinishAcquisitionAsync();
            workspace.Projects.RefreshRecordings();
            sessionWindow?.CloseCompletedSession();
        }
        catch (Exception exception)
        {
            workspace.Notifications.PublishError(exception.Message);
        }
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace || !workspace.Acquisition.LiveMonitor.IsRecording)
        {
            return;
        }

        var shortcut = BuildShortcut(e);
        var definition = workspace.EventDefinitions.FilteredDefinitions.FirstOrDefault(item =>
            item.IsEnabled && item.ShortcutScope == ShortcutScope.Acquisition &&
            string.Equals(NormalizeShortcut(item.Shortcut), shortcut, StringComparison.OrdinalIgnoreCase));
        if (definition is null) return;
        e.Handled = true;
        try
        {
            await workspace.Acquisition.MarkEventAsync(definition.Id, EventSource.KeyboardShortcut, shortcut);
        }
        catch (Exception exception)
        {
            workspace.Notifications.PublishError(exception.Message);
        }
    }

    private static string BuildShortcut(KeyEventArgs e)
    {
        var parts = new List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("CTRL");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("ALT");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("SHIFT");
        parts.Add(e.Key.ToString().ToUpperInvariant());
        return string.Join('+', parts);
    }

    private static string NormalizeShortcut(string? value) => string.Join('+',
        (value ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.ToUpperInvariant()));
}
