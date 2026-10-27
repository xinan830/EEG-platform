using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Modules.Montages.Views;

public partial class MontageDetailView : UserControl
{
    private MontageDirectionWindow? directionWindow;

    public MontageDetailView()
    {
        InitializeComponent();
    }

    private void OnOpenDirectionMapClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace)
        {
            return;
        }
        if (directionWindow is { IsLoaded: true })
        {
            directionWindow.Close();
        }
        try
        {
            directionWindow = new MontageDirectionWindow(workspace.MontageConfigurations)
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            directionWindow.Closed += (_, _) => directionWindow = null;
            directionWindow.Show();
        }
        catch (InvalidOperationException exception)
        {
            workspace.Notifications.PublishError(exception.Message);
        }
    }

    private void OnBackToMontageListClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowMontageListView();
        }
    }

    private void OnCancelMontageDraftClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            viewModel.MontageConfigurations.CloseDraft();
        }

        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowMontageListView();
        }
    }

    private void OnCopyMontageConfigurationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel viewModel ||
            !viewModel.MontageConfigurations.CanCopySelected)
        {
            return;
        }

        try
        {
            viewModel.MontageConfigurations.BeginCopySelected();
        }
        catch (Exception exception)
        {
            viewModel.Notifications.PublishError(exception.Message);
        }
    }
}
