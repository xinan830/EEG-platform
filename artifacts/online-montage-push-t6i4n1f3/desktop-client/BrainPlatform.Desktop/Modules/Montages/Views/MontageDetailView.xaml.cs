using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Modules.Montages.Views;

public partial class MontageDetailView : UserControl
{
    private MontageDirectionWindow? directionWindow;
    public event EventHandler? SessionCloseRequested;
    private MontageConfigurationWorkspaceViewModel? Editor => DataContext switch
    {
        DesktopWorkspaceViewModel workspace => workspace.MontageConfigurations,
        TemporaryMontageEditorContext temporary => temporary.MontageConfigurations,
        _ => null,
    };

    public MontageDetailView()
    {
        InitializeComponent();
        Unloaded += (_, _) => directionWindow?.Close();
    }

    private void OnOpenDirectionMapClick(object sender, RoutedEventArgs e)
    {
        if (Editor is not { } editor)
        {
            return;
        }
        if (directionWindow is { IsLoaded: true })
        {
            directionWindow.Close();
        }
        try
        {
            directionWindow = new MontageDirectionWindow(editor)
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            directionWindow.Closed += (_, _) => directionWindow = null;
            directionWindow.Show();
        }
        catch (InvalidOperationException exception)
        {
            if (DataContext is DesktopWorkspaceViewModel workspace) workspace.Notifications.PublishError(exception.Message);
            else MessageBox.Show(Window.GetWindow(this), exception.Message, "导联设置", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnBackToMontageListClick(object sender, RoutedEventArgs e)
    {
        if (Editor?.IsSessionDraft == true) { SessionCloseRequested?.Invoke(this, EventArgs.Empty); return; }
        if (Window.GetWindow(this) is IDesktopNavigation navigation)
        {
            navigation.ShowMontageListView();
        }
    }

    private void OnCancelMontageDraftClick(object sender, RoutedEventArgs e)
    {
        if (Editor?.IsSessionDraft == true) { SessionCloseRequested?.Invoke(this, EventArgs.Empty); return; }
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            viewModel.MontageConfigurations.CloseDraft();
        }

        if (Window.GetWindow(this) is IDesktopNavigation navigation)
        {
            navigation.ShowMontageListView();
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
