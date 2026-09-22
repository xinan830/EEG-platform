using System.ComponentModel;
using System.Windows;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Views;

/// <summary>
/// Hosts one immersive acquisition or review surface. A close is never just a UI
/// close: it is confirmed first, then its owner performs the session cleanup.
/// </summary>
public partial class EegSessionWindow : Window
{
    private readonly Window ownerWindow;
    private readonly SessionCloseRequest closeRequest;
    private readonly Func<Task> stopSessionAsync;
    private readonly Action? closed;
    private bool closeApproved;
    private bool closeInProgress;

    public EegSessionWindow(
        Window owner,
        string title,
        UIElement content,
        SessionCloseRequest closeRequest,
        Func<Task> stopSessionAsync,
        Action? closed = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(closeRequest);
        ArgumentNullException.ThrowIfNull(stopSessionAsync);

        ownerWindow = owner;
        this.closeRequest = closeRequest;
        this.stopSessionAsync = stopSessionAsync;
        this.closed = closed;
        InitializeComponent();
        Owner = owner;
        Title = title;
        if (content is FrameworkElement frameworkElement)
        {
            DataContext = frameworkElement.DataContext;
        }
        SessionContentHost.Content = content;
        Closed += OnSessionClosed;
    }

    /// <summary>For an explicit in-page completion after the runtime has already stopped.</summary>
    public void CloseCompletedSession()
    {
        closeApproved = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeApproved)
        {
            return;
        }

        e.Cancel = true;
        if (closeInProgress)
        {
            return;
        }

        // Closing is still on WPF's teardown stack here. Even after cancelling it,
        // ShowDialog/Close are illegal until this event has returned to Dispatcher.
        closeInProgress = true;
        Dispatcher.BeginInvoke(() => _ = ConfirmStopAndCloseAsync());
    }

    private async Task ConfirmStopAndCloseAsync()
    {
        if (!SessionCloseConfirmationDialog.Confirm(this, closeRequest))
        {
            closeInProgress = false;
            return;
        }

        IsEnabled = false;
        try
        {
            await stopSessionAsync();
            closeApproved = true;
            Close();
        }
        catch (Exception exception)
        {
            if (DataContext is DesktopWorkspaceViewModel workspace)
            {
                workspace.Notifications.PublishError($"无法关闭会话：{exception.Message}");
            }
            else
            {
                // This path is only a construction-time fallback, when the shared
                // notification state is unavailable.
                MessageBox.Show(this, exception.Message, "无法关闭会话", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            IsEnabled = true;
            closeInProgress = false;
        }
    }

    private void OnSessionClosed(object? sender, EventArgs e)
    {
        closed?.Invoke();

        // The session window is owned by the main platform window. Restore the
        // owner explicitly so closing a maximized session cannot leave the
        // platform minimized or hidden behind other windows.
        if (!ownerWindow.Dispatcher.HasShutdownStarted && !ownerWindow.Dispatcher.HasShutdownFinished)
        {
            ownerWindow.Dispatcher.BeginInvoke(() =>
            {
                if (ownerWindow.IsLoaded)
                {
                    if (ownerWindow.WindowState == WindowState.Minimized)
                    {
                        ownerWindow.WindowState = WindowState.Normal;
                    }

                    if (ownerWindow.Visibility != Visibility.Visible)
                    {
                        ownerWindow.Show();
                    }

                    ownerWindow.Activate();
                }
            });
        }
    }
}
