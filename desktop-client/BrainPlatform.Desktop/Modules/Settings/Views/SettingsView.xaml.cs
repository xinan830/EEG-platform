using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Modules.Settings.Views;

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

    private void OnOpenScreenCalibrationClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowScreenCalibrationView();
        }
    }

    private void OnOpenEventListClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowEventListView();
        }
    }
}
