using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Modules.Channels.Views;

public partial class ChannelListView : UserControl
{
    public ChannelListView()
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

    private void OnChannelPageRequested(object sender, PageRequestedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            viewModel.ChannelConfigurations.CurrentPage = e.Page;
        }
    }

    private void OnOpenChannelDetailClick(object sender, RoutedEventArgs e)
    {
        SelectProfile(sender);
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowChannelDetailView();
        }
    }

    private void OnNewChannelConfigurationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel viewModel)
        {
            return;
        }

        viewModel.ChannelConfigurations.BeginNewProfile(showDevicePicker: false);
        var picker = new DeviceSelectionDialog(viewModel.Acquisition.Devices)
        {
            Owner = Window.GetWindow(this),
        };

        if (picker.ShowDialog() != true || picker.SelectedDevice is null)
        {
            viewModel.ChannelConfigurations.CloseDraft();
            return;
        }

        try
        {
            viewModel.ChannelConfigurations.PendingDeviceSelection = picker.SelectedDevice;
            viewModel.ChannelConfigurations.ConfirmPendingDeviceSelection();
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                mainWindow.ShowChannelDetailView(preserveDraft: true);
            }
        }
        catch (Exception exception)
        {
            viewModel.ChannelConfigurations.CloseDraft();
            viewModel.Notifications.PublishError(exception.Message);
        }
    }

    private void OnConfirmNewChannelDeviceClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel viewModel)
        {
            return;
        }

        try
        {
            viewModel.ChannelConfigurations.ConfirmPendingDeviceSelection();
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                mainWindow.ShowChannelDetailView(preserveDraft: true);
            }
        }
        catch (Exception exception)
        {
            viewModel.Notifications.PublishError(exception.Message);
        }
    }

    private void OnCancelNewChannelDeviceClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            viewModel.ChannelConfigurations.CloseDraft();
        }
    }

    private async void OnDeleteProfileClick(object sender, RoutedEventArgs e)
    {
        SelectProfile(sender);
        if (DataContext is not DesktopWorkspaceViewModel viewModel ||
            !viewModel.ChannelConfigurations.CanDeleteSelected ||
            viewModel.ChannelConfigurations.SelectedProfile is not { } profile)
        {
            return;
        }

        if (!OperationConfirmationDialog.Confirm(
                Window.GetWindow(this),
                OperationConfirmationRequest.DeleteChannelConfiguration(profile.Name)))
        {
            return;
        }

        try
        {
            await viewModel.ChannelConfigurations.DeleteSelectedAsync();
        }
        catch (Exception exception)
        {
            viewModel.Notifications.PublishError(exception.Message);
        }
    }

    private void OnCopyProfileClick(object sender, RoutedEventArgs e)
    {
        SelectProfile(sender);
        if (DataContext is DesktopWorkspaceViewModel viewModel && Window.GetWindow(this) is MainWindow mainWindow)
        {
            try
            {
                viewModel.ChannelConfigurations.BeginCopySelected();
                mainWindow.ShowChannelDetailView(preserveDraft: true);
            }
            catch (Exception exception)
            {
                viewModel.Notifications.PublishError(exception.Message);
            }
        }
    }

    private void SelectProfile(object sender)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is ChannelConfigurationProfile profile &&
            DataContext is DesktopWorkspaceViewModel viewModel)
        {
            viewModel.ChannelConfigurations.SelectedProfile = profile;
        }
    }
}
