using System.ComponentModel;
using System.Windows;

namespace BrainPlatform.Desktop.Views;

/// <summary>
/// Hosts one immersive acquisition or review surface. A close is never just a UI
/// close: it is confirmed first, then its owner performs the session cleanup.
/// </summary>
public partial class EegSessionWindow : Window
{
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

        this.closeRequest = closeRequest;
        this.stopSessionAsync = stopSessionAsync;
        this.closed = closed;
        InitializeComponent();
        Owner = owner;
        Title = title;
        SessionContentHost.Content = content;
        Closed += (_, _) => this.closed?.Invoke();
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
            MessageBox.Show(this, exception.Message, "无法关闭会话", MessageBoxButton.OK, MessageBoxImage.Error);
            IsEnabled = true;
            closeInProgress = false;
        }
    }
}
