using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnOpenChannelListClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowChannelListView();
        }
    }

    private void OnOpenMontageListClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowMontageListView();
        }
    }
}

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

        // A device is selected before opening the editor. This must be a native
        // owner-centred window, rather than an overlay scoped to this list
        // control: the list can be resized, scrolled, or hosted in another view.
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

public partial class ChannelDetailView : UserControl
{
    public ChannelDetailView()
    {
        InitializeComponent();
    }

    private void OnBackToChannelListClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowChannelListView();
        }
    }

    private void OnCancelDeviceSelectionClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            // Cancelling the picker must not discard the channel-configuration
            // draft or navigate away from its editor.
            viewModel.ChannelConfigurations.CancelDeviceSelection();
        }
    }

    private void OnCancelDevicePickerClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            viewModel.ChannelConfigurations.CancelDeviceSelection();

            // Device selection is the mandatory first step of a new mapping.
            // Cancelling before a device is confirmed abandons that empty draft.
            if (viewModel.ChannelConfigurations.DraftDevice is null &&
                Window.GetWindow(this) is MainWindow mainWindow)
            {
                mainWindow.ShowChannelListView();
            }
        }
    }

    private void OnEditDraftRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            var editor = FindVisualChild<TextBox>(element.Parent as DependencyObject);
            editor?.Focus();
            editor?.SelectAll();
        }
    }

    private void OnCreateEditableCopyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel viewModel &&
            viewModel.ChannelConfigurations.CanCreateVersionSelected)
        {
            viewModel.ChannelConfigurations.BeginNewVersionSelected();
        }
    }

    private void OnCopyConfigurationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel viewModel)
        {
            return;
        }

        try
        {
            viewModel.ChannelConfigurations.BeginCopySelected();
        }
        catch (Exception exception)
        {
            viewModel.Notifications.PublishError(exception.Message);
        }
    }

    private async void OnDeleteConfigurationClick(object sender, RoutedEventArgs e)
    {
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
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                mainWindow.ShowChannelListView();
            }
        }
        catch (Exception exception)
        {
            viewModel.Notifications.PublishError(exception.Message);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent)
        where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

}

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
