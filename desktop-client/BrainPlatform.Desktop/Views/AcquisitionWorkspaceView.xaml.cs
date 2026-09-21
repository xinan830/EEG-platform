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

        var window = Window.GetWindow(this) as MainWindow;
        try
        {
            await workspace.Acquisition.FinishAcquisitionAsync();
            workspace.Projects.RefreshRecordings();
            window?.ShowProjectDetailView();
        }
        catch (Exception exception)
        {
            workspace.Notifications.PublishError(exception.Message);
        }
    }
}
