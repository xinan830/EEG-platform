using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Views;

public partial class MontageDetailView : UserControl
{
    public MontageDetailView()
    {
        InitializeComponent();
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
