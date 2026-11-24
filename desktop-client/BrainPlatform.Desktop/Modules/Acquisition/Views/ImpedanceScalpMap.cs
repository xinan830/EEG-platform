using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Modules.Acquisition.Views;

/// <summary>Draws measured electrodes individually; deliberately does not interpolate.</summary>
public sealed class ImpedanceScalpMap : Grid
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(nameof(Rows),
        typeof(ObservableCollection<ImpedanceElectrodeRow>), typeof(ImpedanceScalpMap), new PropertyMetadata(null, OnRowsChanged));
    public static readonly DependencyProperty SelectedRowProperty = DependencyProperty.Register(nameof(SelectedRow),
        typeof(ImpedanceElectrodeRow), typeof(ImpedanceScalpMap),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (sender, _) => ((ImpedanceScalpMap)sender).UpdateSelection()));

    private readonly Canvas markers = new();
    private readonly Dictionary<ImpedanceElectrodeRow, ElectrodeMarker> markersByRow = [];

    public ImpedanceScalpMap()
    {
        Children.Add(new HeadOutline());
        Children.Add(markers);
        SizeChanged += (_, _) => RepositionMarkers();
    }

    public ObservableCollection<ImpedanceElectrodeRow>? Rows
    {
        get => (ObservableCollection<ImpedanceElectrodeRow>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public ImpedanceElectrodeRow? SelectedRow
    {
        get => (ImpedanceElectrodeRow?)GetValue(SelectedRowProperty);
        set => SetValue(SelectedRowProperty, value);
    }

    private static void OnRowsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (ImpedanceScalpMap)sender;
        if (args.OldValue is ObservableCollection<ImpedanceElectrodeRow> old) old.CollectionChanged -= control.OnCollectionChanged;
        if (args.NewValue is ObservableCollection<ImpedanceElectrodeRow> next) next.CollectionChanged += control.OnCollectionChanged;
        control.RebuildAll();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems is not null)
        {
            var bounds = ScalpMapRenderer.GetHeadBounds(ActualWidth, ActualHeight, 5, 24, 0.48);
            var diameter = bounds.IsEmpty ? 0 : ElectrodeMarker.Diameter(bounds);
            foreach (var row in args.NewItems.OfType<ImpedanceElectrodeRow>())
            {
                if (row.Position is null || markersByRow.ContainsKey(row)) continue;
                var marker = CreateMarker(row);
                markersByRow[row] = marker;
                markers.Children.Add(marker);
                if (!bounds.IsEmpty) PositionMarker(marker, row.Position, bounds, diameter);
            }
            UpdateSelection();
            return;
        }

        RebuildAll();
    }

    private void RebuildAll()
    {
        markers.Children.Clear();
        markersByRow.Clear();
        if (Rows is null) return;
        foreach (var row in Rows.Where(row => row.Position is not null))
        {
            var marker = CreateMarker(row);
            markersByRow[row] = marker;
            markers.Children.Add(marker);
        }
        RepositionMarkers();
        UpdateSelection();
    }

    private ElectrodeMarker CreateMarker(ImpedanceElectrodeRow row)
    {
        var button = new ElectrodeMarker { Label = row.Label, Tag = row };
        button.SetBinding(ElectrodeMarker.ReadingProperty, new Binding(nameof(row.ValueText)) { Source = row });
        button.SetBinding(BackgroundProperty, new Binding(nameof(row.Color)) { Source = row });
        button.SetBinding(ToolTipProperty, new Binding(nameof(row.AccessibleText)) { Source = row });
        button.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding(nameof(row.AccessibleText)) { Source = row });
        button.Click += (_, _) => SelectedRow = row;
        return button;
    }

    private void RepositionMarkers()
    {
        if (markersByRow.Count == 0) return;
        var bounds = ScalpMapRenderer.GetHeadBounds(ActualWidth, ActualHeight, 5, 24, 0.48);
        if (bounds.IsEmpty) return;
        var diameter = ElectrodeMarker.Diameter(bounds);
        foreach (var (row, marker) in markersByRow)
        {
            if (row.Position is { } pos) PositionMarker(marker, pos, bounds, diameter);
        }
    }

    private static void PositionMarker(ElectrodeMarker marker, ElectrodeMapPosition pos, Rect bounds, double diameter)
    {
        marker.Width = diameter + 16;
        marker.Height = diameter + 20;
        var point = ScalpMapRenderer.ToCanvas(pos.X, pos.Y, bounds);
        Canvas.SetLeft(marker, point.X - marker.Width / 2);
        Canvas.SetTop(marker, point.Y - diameter / 2 - 20);
    }

    private void UpdateSelection()
    {
        foreach (var button in markersByRow.Values)
        {
            button.IsSelected = ReferenceEquals(button.Tag, SelectedRow);
        }
    }

    private sealed class HeadOutline : FrameworkElement
    {
        public HeadOutline() => SizeChanged += (_, _) => InvalidateVisual();
        protected override void OnRender(DrawingContext drawingContext) =>
            ScalpMapRenderer.DrawHead(drawingContext, ActualWidth, ActualHeight, 5, 24,
                VisualTreeHelper.GetDpi(this).PixelsPerDip, radiusScale: 0.48);
    }
}
