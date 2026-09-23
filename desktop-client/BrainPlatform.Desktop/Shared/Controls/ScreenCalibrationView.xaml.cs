using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Shared.Controls;

public partial class ScreenCalibrationView : UserControl
{
    public ScreenCalibrationView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            window.LocationChanged += OnWindowLocationChanged;
            UpdateDisplayContext(window);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is BrainPlatform.Desktop.ViewModels.DesktopWorkspaceViewModel workspace)
        {
            workspace.ScreenCalibration.PersistValidDraftBeforeLeaving();
        }

        if (Window.GetWindow(this) is { } window)
        {
            window.LocationChanged -= OnWindowLocationChanged;
        }
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            UpdateDisplayContext(window);
        }
    }

    private void UpdateDisplayContext(Window window)
    {
        if (DataContext is BrainPlatform.Desktop.ViewModels.DesktopWorkspaceViewModel workspace)
        {
            workspace.ScreenCalibration.UpdateDisplayContext(
                ScreenCalibrationMetrics.GetDisplayMetrics(window));
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowSettingsView();
        }
    }
}
