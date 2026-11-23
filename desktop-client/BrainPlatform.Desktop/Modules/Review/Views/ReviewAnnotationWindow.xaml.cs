using System.Windows;
using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Events.Domain;

namespace BrainPlatform.Desktop.Modules.Review.Views;

/// <summary>Event commands share the review model; WindowChrome owns title-bar dragging and resizing.</summary>
public partial class ReviewAnnotationWindow : Window
{
    public ReviewAnnotationWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            EventGrid.RefreshEmptyState();
            SyncTabVisibility();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || DataContext is not RecordingReviewViewModel model) return;
            if (model.IsEventPlacementActive)
            {
                model.CancelEventPlacement();
                e.Handled = true;
            }
            else if (model.HasActiveInterval)
            {
                model.CancelActiveInterval();
                e.Handled = true;
            }
        };
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnTabSelectionChanged(object sender, RoutedEventArgs e) => SyncTabVisibility();

    private void OnSwitchToAnnotateClick(object sender, RoutedEventArgs e)
    {
        AnnotateEditTab.IsChecked = true;
    }

    private void SyncTabVisibility()
    {
        if (EventListPanel is null || AnnotateEditPanel is null) return;
        var showList = EventListTab.IsChecked == true;
        EventListPanel.Visibility = showList ? Visibility.Visible : Visibility.Collapsed;
        AnnotateEditPanel.Visibility = showList ? Visibility.Collapsed : Visibility.Visible;
        if (!showList)
        {
            EventGrid.RefreshEmptyState();
        }
    }

    private async void OnEventTileActivated(object? sender, EventDefinition definition)
    {
        if (DataContext is not RecordingReviewViewModel model) return;
        var wasCompletingOrPoint = !definition.IsIntervalKind || model.HasActiveInterval;
        try
        {
            await model.TriggerEventFromGridAsync(definition, EventGrid.NoteText);
            if (wasCompletingOrPoint && !model.HasActiveInterval)
            {
                EventGrid.NoteText = string.Empty;
            }
        }
        catch (Exception exception)
        {
            model.ReportEventShortcutError(exception);
        }
    }

    private void OnActiveIntervalCancelled(object? sender, EventArgs e)
    {
        if (DataContext is RecordingReviewViewModel model)
        {
            model.CancelActiveInterval();
        }
    }

    private async void OnRowSeekClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecordingReviewViewModel model) return;
        if (sender is not FrameworkElement { Tag: RecordingEvent item }) return;
        model.SelectedRecordingEvent = item;
        try { await model.SeekToEventAsync(item); }
        catch (Exception exception) { model.ReportEventShortcutError(exception); }
    }

    private void OnRowEditClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecordingReviewViewModel model) return;
        if (sender is not FrameworkElement { Tag: RecordingEvent item }) return;
        model.SelectedRecordingEvent = item;
        if (model.CanEditSelectedEvent)
        {
            model.IsEventEditorOpen = true;
            AnnotateEditTab.IsChecked = true;
        }
    }

    private async void OnRowDeleteClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecordingReviewViewModel model) return;
        if (sender is not FrameworkElement { Tag: RecordingEvent item }) return;
        model.SelectedRecordingEvent = item;
        try { await model.DeleteSelectedEventAsync(); }
        catch (Exception exception) { model.ReportEventShortcutError(exception); }
    }
}
