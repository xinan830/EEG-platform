using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Modules.Events.Domain;
using BrainPlatform.Desktop.Modules.Events.ViewModels;

namespace BrainPlatform.Desktop.Modules.Events.Views;

/// <summary>
/// Shared 3-column icon-grid event picker used by both Acquisition and Review
/// annotation popups. Emits TileActivated when the operator clicks an event tile.
/// </summary>
public partial class EventAnnotationGridControl : UserControl
{
    public static readonly DependencyProperty TilesProperty = DependencyProperty.Register(
        nameof(Tiles), typeof(ObservableCollection<EventGridTileItem>), typeof(EventAnnotationGridControl),
        new PropertyMetadata(null, OnTilesChanged));

    public static readonly DependencyProperty HasNoTilesProperty = DependencyProperty.Register(
        nameof(HasNoTiles), typeof(bool), typeof(EventAnnotationGridControl), new PropertyMetadata(true));

    public static readonly DependencyProperty HasActiveIntervalProperty = DependencyProperty.Register(
        nameof(HasActiveInterval), typeof(bool), typeof(EventAnnotationGridControl), new PropertyMetadata(false));

    public static readonly DependencyProperty ActiveIntervalStatusTextProperty = DependencyProperty.Register(
        nameof(ActiveIntervalStatusText), typeof(string), typeof(EventAnnotationGridControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty NoteTextProperty = DependencyProperty.Register(
        nameof(NoteText), typeof(string), typeof(EventAnnotationGridControl), new PropertyMetadata(string.Empty));

    public EventAnnotationGridControl()
    {
        InitializeComponent();
    }

    public ObservableCollection<EventGridTileItem>? Tiles
    {
        get => (ObservableCollection<EventGridTileItem>?)GetValue(TilesProperty);
        set => SetValue(TilesProperty, value);
    }

    public bool HasNoTiles
    {
        get => (bool)GetValue(HasNoTilesProperty);
        private set => SetValue(HasNoTilesProperty, value);
    }

    public bool HasActiveInterval
    {
        get => (bool)GetValue(HasActiveIntervalProperty);
        set => SetValue(HasActiveIntervalProperty, value);
    }

    public string ActiveIntervalStatusText
    {
        get => (string)GetValue(ActiveIntervalStatusTextProperty);
        set => SetValue(ActiveIntervalStatusTextProperty, value);
    }

    public string NoteText
    {
        get => (string)GetValue(NoteTextProperty);
        set => SetValue(NoteTextProperty, value);
    }

    public event EventHandler<EventDefinition>? TileActivated;

    public event EventHandler? ActiveIntervalCancelled;

    public void RefreshEmptyState()
    {
        HasNoTiles = Tiles is null || Tiles.Count == 0;
    }

    private static void OnTilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EventAnnotationGridControl control)
        {
            control.RefreshEmptyState();
        }
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: EventGridTileItem tile })
        {
            TileActivated?.Invoke(this, tile.Definition);
        }
    }

    private void OnCancelActiveIntervalClick(object sender, RoutedEventArgs e) =>
        ActiveIntervalCancelled?.Invoke(this, EventArgs.Empty);
}
