using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Events;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Views;

public partial class EventListView : UserControl
{
    public EventListView() => InitializeComponent();

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow) mainWindow.ShowSettingsView();
    }

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel workspace) workspace.EventDefinitions.BeginNew();
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace || sender is not FrameworkElement { DataContext: EventDefinition definition }) return;
        workspace.EventDefinitions.SelectedDefinition = definition;
        workspace.EventDefinitions.BeginEditSelected();
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace || sender is not FrameworkElement { DataContext: EventDefinition definition }) return;
        workspace.EventDefinitions.SelectedDefinition = definition;
        try { await workspace.EventDefinitions.DeleteSelectedAsync(); }
        catch (Exception exception) { workspace.Notifications.PublishError(exception.Message); }
    }
}
