using System.Windows;
using System.Windows.Controls;
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
}
