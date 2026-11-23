using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.ViewModels;
using BrainPlatform.Desktop.Shared.Controls;

namespace BrainPlatform.Desktop.Modules.Channels.Views;

/// <summary>Draws persisted standard-template scalp positions without mapping signal values.</summary>
public sealed class ElectrodePositionMap : FrameworkElement
{
    private const double LandmarkHitRadius = 12;
    internal const double HeadRadiusScale = 0.46;
    public static readonly DependencyProperty IsTopographicProperty = DependencyProperty.Register(
        nameof(IsTopographic), typeof(bool), typeof(ElectrodePositionMap),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(IEnumerable<ChannelConfigurationDraftRow>), typeof(ElectrodePositionMap),
        new FrameworkPropertyMetadata(null, OnRowsChanged));
    public static readonly DependencyProperty SelectedRowProperty = DependencyProperty.Register(
        nameof(SelectedRow), typeof(ChannelConfigurationDraftRow), typeof(ElectrodePositionMap),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ReferencePositionProperty = DependencyProperty.Register(
        nameof(ReferencePosition), typeof(ElectrodeMapPosition), typeof(ElectrodePositionMap),
        new FrameworkPropertyMetadata(ElectrodeMapDefaults.Reference, OnMapPositionChanged));
    public static readonly DependencyProperty GroundPositionProperty = DependencyProperty.Register(
        nameof(GroundPosition), typeof(ElectrodeMapPosition), typeof(ElectrodePositionMap),
        new FrameworkPropertyMetadata(ElectrodeMapDefaults.Ground, OnMapPositionChanged));

    private INotifyCollectionChanged? observedCollection;
    private ChannelConfigurationDraftRow[] observedRows = [];
    private string? draggedLandmark;

    public bool IsTopographic
    {
        get => (bool)GetValue(IsTopographicProperty);
        set => SetValue(IsTopographicProperty, value);
    }

    public IEnumerable<ChannelConfigurationDraftRow>? Rows
    {
        get => (IEnumerable<ChannelConfigurationDraftRow>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public ChannelConfigurationDraftRow? SelectedRow
    {
        get => (ChannelConfigurationDraftRow?)GetValue(SelectedRowProperty);
        set => SetValue(SelectedRowProperty, value);
    }

    public ElectrodeMapPosition ReferencePosition
    {
        get => (ElectrodeMapPosition)GetValue(ReferencePositionProperty);
        set => SetValue(ReferencePositionProperty, value);
    }

    public ElectrodeMapPosition GroundPosition
    {
        get => (ElectrodeMapPosition)GetValue(GroundPositionProperty);
        set => SetValue(GroundPositionProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var headBounds = ScalpMapRenderer.DrawHead(drawingContext, ActualWidth, ActualHeight,
            ScalpMapRenderer.VerticalOffset, ScalpMapRenderer.ReservedHeight,
            VisualTreeHelper.GetDpi(this).PixelsPerDip, radiusScale: HeadRadiusScale);
        if (headBounds.IsEmpty) return;

        foreach (var point in ElectrodeMapProjection.ProjectWithHardwareLandmarks(Rows ?? [], ReferencePosition, GroundPosition))
        {
            var position = ScalpMapRenderer.ToCanvas(point.X, point.Y, headBounds);
            if (IsTopographic)
            {
                ScalpMapRenderer.DrawElectrodeDot(drawingContext, position);
                var label = new FormattedText(point.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 12, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                drawingContext.DrawText(label, new Point(position.X - label.Width / 2, position.Y + 5));
                continue;
            }
            var isManual = (Rows ?? []).FirstOrDefault(row => row.ElectrodeLabel == point.Label)?.ManualMapPosition is not null;
            ScalpMapRenderer.DrawElectrode(drawingContext, point.Label, position, point.IsHardwareLandmark, isManual,
                VisualTreeHelper.GetDpi(this).PixelsPerDip, ElectrodeMarker.Diameter(headBounds),
                !point.IsHardwareLandmark && SelectedRow?.ElectrodeLabel == point.Label);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pointer = e.GetPosition(this);
        draggedLandmark = HitTestHardwareLandmark(
            pointer, ActualWidth, ActualHeight, ReferencePosition, GroundPosition,
            Math.Max(LandmarkHitRadius, ElectrodeMarker.Diameter(GetHeadBounds(ActualWidth, ActualHeight)) / 2));
        if (draggedLandmark is not null)
        {
            CaptureMouse();
            MoveHardwareLandmark(pointer);
            e.Handled = true;
            return;
        }

        if (CanPlaceRow(SelectedRow))
        {
            CaptureMouse();
            PlaceSelectedRow(pointer);
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            var pointer = e.GetPosition(this);
            if (draggedLandmark is not null)
            {
                MoveHardwareLandmark(pointer);
            }
            else
            {
                PlaceSelectedRow(pointer);
            }
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured)
        {
            draggedLandmark = null;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void PlaceSelectedRow(Point pointer)
    {
        if (SelectedRow is not { } row || !CanPlaceRow(row) ||
            ToMapPosition(pointer, ActualWidth, ActualHeight) is not { } position)
        {
            return;
        }
        row.SetManualMapPosition(position);
        InvalidateVisual();
    }

    private void MoveHardwareLandmark(Point pointer)
    {
        if (ToMapPosition(pointer, ActualWidth, ActualHeight) is not { } position)
        {
            return;
        }

        if (draggedLandmark == "REF")
        {
            ReferencePosition = position;
        }
        else if (draggedLandmark == "GND")
        {
            GroundPosition = position;
        }
        InvalidateVisual();
    }

    internal static string? HitTestHardwareLandmark(
        Point pointer,
        double width,
        double height,
        ElectrodeMapPosition referencePosition,
        ElectrodeMapPosition groundPosition,
        double hitRadius)
    {
        var bounds = GetHeadBounds(width, height);
        if (bounds.IsEmpty || hitRadius < 0)
        {
            return null;
        }

        referencePosition = ElectrodeMapDefaults.ReferenceForDisplay(referencePosition);
        var reference = ScalpMapRenderer.ToCanvas(referencePosition.X, referencePosition.Y, bounds);
        var ground = ScalpMapRenderer.ToCanvas(groundPosition.X, groundPosition.Y, bounds);
        var referenceDistance = (pointer - reference).Length;
        var groundDistance = (pointer - ground).Length;
        if (referenceDistance > hitRadius && groundDistance > hitRadius)
        {
            return null;
        }
        return referenceDistance <= groundDistance ? "REF" : "GND";
    }

    internal static Rect GetHeadBounds(double width, double height) => ScalpMapRenderer.GetHeadBounds(
        width, height, ScalpMapRenderer.VerticalOffset, ScalpMapRenderer.ReservedHeight, HeadRadiusScale);

    internal static ElectrodeMapPosition? ToMapPosition(Point pointer, double width, double height)
    {
        var bounds = GetHeadBounds(width, height);
        if (bounds.IsEmpty) return null;
        var radius = bounds.Width / 2;
        var x = Math.Clamp((pointer.X - bounds.X - radius) / radius, -1, 1);
        var y = Math.Clamp((pointer.Y - bounds.Y - radius) / radius, -1, 1);
        return x * x + y * y <= 1 ? new ElectrodeMapPosition(x, y) : null;
    }

    internal static bool CanPlaceRow(ChannelConfigurationDraftRow? row) => row is { IsEegInput: true };

    private static void OnRowsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var map = (ElectrodePositionMap)sender;
        if (map.observedCollection is not null)
        {
            map.observedCollection.CollectionChanged -= map.OnCollectionChanged;
        }
        map.UnobserveRows();
        map.observedCollection = args.NewValue as INotifyCollectionChanged;
        if (map.observedCollection is not null)
        {
            map.observedCollection.CollectionChanged += map.OnCollectionChanged;
        }
        map.ObserveRows();
        map.InvalidateVisual();
    }

    private static void OnMapPositionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ElectrodePositionMap)sender).InvalidateVisual();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        UnobserveRows();
        ObserveRows();
        InvalidateVisual();
    }

    private void ObserveRows()
    {
        observedRows = (Rows ?? []).ToArray();
        foreach (var row in observedRows)
        {
            row.PropertyChanged += OnRowChanged;
        }
    }

    private void UnobserveRows()
    {
        foreach (var row in observedRows)
        {
            row.PropertyChanged -= OnRowChanged;
        }
        observedRows = [];
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ChannelConfigurationDraftRow.ElectrodeLabel) or
            nameof(ChannelConfigurationDraftRow.ElectrodeLocation) or
            nameof(ChannelConfigurationDraftRow.ManualMapPosition) or
            nameof(ChannelConfigurationDraftRow.IsEnabled))
        {
            InvalidateVisual();
        }
    }
}
