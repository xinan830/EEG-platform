using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Views;

public partial class AcquisitionPreparationView : UserControl
{
    private bool startInProgress;

    public AcquisitionPreparationView()
    {
        InitializeComponent();
    }

    private void OnBackClick(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.ShowProjectListView();

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (startInProgress || DataContext is not DesktopWorkspaceViewModel workspace)
        {
            return;
        }

        var window = Window.GetWindow(this) as MainWindow;
        startInProgress = true;
        try
        {
            await workspace.Acquisition.StartPreparedPreviewAsync();
            window?.ShowAcquisitionView();
        }
        catch (Exception exception)
        {
            workspace.Notifications.PublishError(exception.Message);
        }
        finally
        {
            startInProgress = false;
        }
    }
}
