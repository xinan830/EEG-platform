using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Modules.Channels.Views;

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
            viewModel.ChannelConfigurations.CancelDeviceSelection();
        }
    }

    private void OnCancelDevicePickerClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            viewModel.ChannelConfigurations.CancelDeviceSelection();
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
