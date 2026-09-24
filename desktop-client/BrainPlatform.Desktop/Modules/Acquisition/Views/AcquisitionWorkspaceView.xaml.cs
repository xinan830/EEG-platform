using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Events.Views;

namespace BrainPlatform.Desktop.Modules.Acquisition.Views;

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

        var definition = EventShortcutInput.Match(
            workspace.Acquisition.EnabledEventDefinitions, ShortcutScope.Acquisition, e, out var shortcut);
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

}
