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
    public ImpedanceScalpMap()
    {
        Children.Add(new HeadOutline());
        Children.Add(markers);
        SizeChanged += (_, _) => Rebuild();
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
        control.Rebuild();
    }
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => Rebuild();
    private void Rebuild()
    {
        markers.Children.Clear();
        if (Rows is null) return;
        var bounds = ScalpMapRenderer.GetHeadBounds(ActualWidth, ActualHeight, 5, 24);
        if (bounds.IsEmpty) return;
        var diameter = ElectrodeMarker.Diameter(bounds);
        foreach (var row in Rows.Where(row => row.Position is not null))
        {
            var button = new ElectrodeMarker
            {
                Label = row.Label, Width = diameter + 16, Height = diameter + 20, Tag = row,
            };
            button.SetBinding(ElectrodeMarker.ReadingProperty, new Binding(nameof(row.ValueText)) { Source = row });
            button.SetBinding(BackgroundProperty, new Binding(nameof(row.Color)) { Source = row });
            button.SetBinding(ToolTipProperty, new Binding(nameof(row.AccessibleText)) { Source = row });
            button.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding(nameof(row.AccessibleText)) { Source = row });
            button.Click += (_, _) => SelectedRow = row;
            var point = ScalpMapRenderer.ToCanvas(row.Position!.X, row.Position.Y, bounds);
            Canvas.SetLeft(button, point.X - button.Width / 2);
            Canvas.SetTop(button, point.Y - diameter / 2 - 20);
            markers.Children.Add(button);
        }
        UpdateSelection();
    }
    private void UpdateSelection()
    {
        foreach (var button in markers.Children.OfType<ElectrodeMarker>())
        {
            button.IsSelected = ReferenceEquals(button.Tag, SelectedRow);
        }
    }
    private sealed class HeadOutline : FrameworkElement
    {
        public HeadOutline() => SizeChanged += (_, _) => InvalidateVisual();
        protected override void OnRender(DrawingContext drawingContext) =>
            ScalpMapRenderer.DrawHead(drawingContext, ActualWidth, ActualHeight, 5, 24, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}
