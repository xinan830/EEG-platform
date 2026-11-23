using System.Windows;
using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Acquisition.ViewModels;
using BrainPlatform.Desktop.Modules.Events.Domain;

namespace BrainPlatform.Desktop.Modules.Acquisition.Views;

public partial class AcquisitionAnnotationWindow : Window
{
    private readonly OperationNotificationCenter? notifications;

    public AcquisitionAnnotationWindow(OperationNotificationCenter? notifications = null)
    {
        this.notifications = notifications;
        InitializeComponent();
        Loaded += (_, _) => EventGrid.RefreshEmptyState();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is AcquisitionWorkspaceViewModel { HasActiveInterval: true } model)
            {
                model.CancelActiveInterval();
                e.Handled = true;
            }
        };
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private async void OnEventTileActivated(object? sender, EventDefinition definition)
    {
        if (DataContext is not AcquisitionWorkspaceViewModel model)
        {
            return;
        }

        var wasCompletingOrPoint = !definition.IsIntervalKind || model.HasActiveInterval;
        try
        {
            await model.TriggerEventFromGridAsync(definition, EventGrid.NoteText, EventSource.ManualButton, "acquisition-grid");
            if (wasCompletingOrPoint && !model.HasActiveInterval)
            {
                EventGrid.NoteText = string.Empty;
            }
        }
        catch (Exception exception)
        {
            notifications?.PublishError(exception.Message);
        }
    }

    private void OnActiveIntervalCancelled(object? sender, EventArgs e)
    {
        if (DataContext is AcquisitionWorkspaceViewModel model)
        {
            model.CancelActiveInterval();
        }
    }
}
