using System.ComponentModel;
using System.Windows;

namespace BrainPlatform.Desktop.Modules.Acquisition.Views;

public partial class AcquisitionImpedanceWindow : Window
{
    private readonly AcquisitionImpedanceViewModel model;
    private readonly ConfiguredAcquisitionRuntime runtime;
    private readonly Func<Task> startPreview;
    private readonly OperationNotificationCenter? notifications;
    private Task? closingTask;
    private bool closeApproved;
    private bool stopping;
    public AcquisitionImpedanceWindow(AcquisitionImpedanceViewModel model, ConfiguredAcquisitionRuntime runtime,
        Func<Task> startPreview, OperationNotificationCenter? notifications)
    {
        this.model = model;
        this.runtime = runtime;
        this.startPreview = startPreview;
        this.notifications = notifications;
        InitializeComponent();
        DataContext = model;
        Closed += (_, _) => model.Dispose();
    }
    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (closeApproved) return;
        args.Cancel = true;
        Dispatcher.BeginInvoke(() => _ = StopSafelyAsync(false));
    }
    private async void OnStopClick(object sender, RoutedEventArgs args) => await StopSafelyAsync(false);
    private async void OnPreviewClick(object sender, RoutedEventArgs args) => await StopSafelyAsync(true);
    private async Task StopSafelyAsync(bool preview)
    {
        if (stopping) return;
        stopping = true;
        try
        {
            await StopAndCloseAsync();
            if (preview) await startPreview();
        }
        catch (Exception exception)
        {
            if (notifications is not null) notifications.PublishError(exception.Message);
            else MessageBox.Show(exception.Message, "阻抗检测", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { stopping = false; }
    }
    public async Task StopAndCloseAsync()
    {
        var active = closingTask ??= StopCoreAsync();
        try { await active; }
        catch { if (ReferenceEquals(active, closingTask)) closingTask = null; throw; }
    }
    private async Task StopCoreAsync()
    {
        IsEnabled = false;
        try
        {
            await runtime.StopImpedanceAsync();
            closeApproved = true;
            Close();
        }
        catch
        {
            IsEnabled = true;
            closingTask = null;
            throw;
        }
    }
}
