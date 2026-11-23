using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Modules.Acquisition.Views;

public partial class AcquisitionPreparationView : UserControl
{
    private bool startInProgress;

    public AcquisitionPreparationView()
    {
        InitializeComponent();
        Unloaded += async (_, _) =>
        {
            if (DataContext is not DesktopWorkspaceViewModel workspace) return;
            try { await workspace.Acquisition.CloseImpedanceWindowAsync(); }
            catch (Exception exception) { workspace.Notifications.PublishError(exception.Message); }
        };
    }

    private void OnBackClick(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as IDesktopNavigation)?.ShowProjectDetailView();

    private async void OnImpedanceClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace) return;
        try { await workspace.Acquisition.ShowImpedanceAsync(Window.GetWindow(this)); }
        catch (Exception exception) { workspace.Notifications.PublishError(exception.Message); }
    }

    private void OnViewProjectClick(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as IDesktopNavigation)?.ShowProjectDetailView();

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (startInProgress || DataContext is not DesktopWorkspaceViewModel workspace)
        {
            return;
        }

        var navigation = Window.GetWindow(this) as IDesktopNavigation;
        startInProgress = true;
        try
        {
            await workspace.Acquisition.StartPreparedPreviewAsync();
            navigation?.ShowAcquisitionView();
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
