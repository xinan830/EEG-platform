using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Modules.Montages.Views;

public partial class MontageListView : UserControl
{
    public MontageListView()
    {
        InitializeComponent();
    }

    private void OnBackToSettingsClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowSettingsView();
        }
    }

    private void OnMontagePageRequested(object sender, PageRequestedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            viewModel.MontageConfigurations.CurrentPage = e.Page;
        }
    }

    private void OnOpenMontageDetailClick(object sender, RoutedEventArgs e)
    {
        SelectMontageProfile(sender);
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowMontageDetailView();
        }
    }

    private void OnNewMontageProfileClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowMontageDetailView(newProfile: true);
        }
    }

    private async void OnDeleteMontageProfileClick(object sender, RoutedEventArgs e)
    {
        SelectMontageProfile(sender);
        if (DataContext is not DesktopWorkspaceViewModel viewModel ||
            !viewModel.MontageConfigurations.CanDeleteSelected ||
            viewModel.MontageConfigurations.SelectedProfile is not { } profile)
        {
            return;
        }

        if (!OperationConfirmationDialog.Confirm(
                Window.GetWindow(this),
                OperationConfirmationRequest.DeleteMontageConfiguration(profile.Name)))
        {
            return;
        }

        try
        {
            await viewModel.MontageConfigurations.DeleteSelectedAsync();
        }
        catch (Exception exception)
        {
            viewModel.Notifications.PublishError(exception.Message);
        }
    }

    private void OnCopyMontageProfileClick(object sender, RoutedEventArgs e)
    {
        SelectMontageProfile(sender);
        if (DataContext is DesktopWorkspaceViewModel viewModel && Window.GetWindow(this) is MainWindow mainWindow)
        {
            try
            {
                viewModel.MontageConfigurations.BeginCopySelected();
                mainWindow.ShowMontageDetailView(preserveDraft: true);
            }
            catch (Exception exception)
            {
                viewModel.Notifications.PublishError(exception.Message);
            }
        }
    }

    private void SelectMontageProfile(object sender)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is MontageProfile profile &&
            DataContext is DesktopWorkspaceViewModel viewModel)
        {
            viewModel.MontageConfigurations.SelectedProfile = profile;
        }
    }
}
