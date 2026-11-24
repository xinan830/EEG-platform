using System.Windows;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Review.Views;

/// <summary>Event commands share the review model; WindowChrome owns title-bar dragging and resizing.</summary>
public partial class ReviewAnnotationWindow : Window
{
    public ReviewAnnotationWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || DataContext is not RecordingReviewViewModel { IsEventPlacementActive: true } model) return;
            model.CancelEventPlacement();
            e.Handled = true;
        };
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private async void OnSeekClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecordingReviewViewModel { SelectedRecordingEvent: { } item } model) return;
        try { await model.SeekToEventAsync(item); }
        catch (Exception exception) { model.ReportEventShortcutError(exception); }
    }
}
