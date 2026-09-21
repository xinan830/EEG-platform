using System.Windows;
using System.Windows.Controls;
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
}
