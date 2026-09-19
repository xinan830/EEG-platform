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
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowChannelDetailView(newProfile: true);
        }
    }

    private void OnDeleteProfileClick(object sender, RoutedEventArgs e)
    {
        SelectProfile(sender);
        if (DataContext is DesktopWorkspaceViewModel viewModel && viewModel.ChannelConfigurations.CanDeleteSelected)
        {
            viewModel.ChannelConfigurations.DeleteSelectedCommand.Execute(null);
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

    private void OnDeleteMontageProfileClick(object sender, RoutedEventArgs e)
    {
        SelectMontageProfile(sender);
        if (DataContext is DesktopWorkspaceViewModel viewModel && viewModel.MontageConfigurations.CanDeleteSelected)
        {
            viewModel.MontageConfigurations.DeleteSelectedCommand.Execute(null);
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
}
