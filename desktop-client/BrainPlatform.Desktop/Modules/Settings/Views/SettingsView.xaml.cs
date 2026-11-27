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
        if (Window.GetWindow(this) is IDesktopNavigation navigation)
        {
            navigation.ShowChannelListView();
        }
    }

    private void OnOpenMontageListClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is IDesktopNavigation navigation)
        {
            navigation.ShowMontageListView();
        }
    }

    private void OnOpenScreenCalibrationClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is IDesktopNavigation navigation)
        {
            navigation.ShowScreenCalibrationView();
        }
    }

    private void OnOpenEventListClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is IDesktopNavigation navigation)
        {
            navigation.ShowEventListView();
        }
    }

    private void OnOpenBrainImagingHubClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is IDesktopNavigation navigation)
        {
            navigation.ShowBrainImagingHubView();
        }
    }

    private void OnOpenThirdPartyNoticesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ThirdPartyNoticesDialog
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }
}
