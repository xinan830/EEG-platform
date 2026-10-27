using System.Collections.Specialized;
using System.ComponentModel;
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
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(IEnumerable<ChannelConfigurationDraftRow>), typeof(ElectrodePositionMap),
        new FrameworkPropertyMetadata(null, OnRowsChanged));
    public static readonly DependencyProperty SelectedRowProperty = DependencyProperty.Register(
        nameof(SelectedRow), typeof(ChannelConfigurationDraftRow), typeof(ElectrodePositionMap),
        new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty ReferencePositionProperty = DependencyProperty.Register(
        nameof(ReferencePosition), typeof(ElectrodeMapPosition), typeof(ElectrodePositionMap),
        new FrameworkPropertyMetadata(ElectrodeMapDefaults.Reference, OnMapPositionChanged));
    public static readonly DependencyProperty GroundPositionProperty = DependencyProperty.Register(
        nameof(GroundPosition), typeof(ElectrodeMapPosition), typeof(ElectrodePositionMap),
        new FrameworkPropertyMetadata(ElectrodeMapDefaults.Ground, OnMapPositionChanged));

    private INotifyCollectionChanged? observedCollection;
    private ChannelConfigurationDraftRow[] observedRows = [];
    private string? draggedLandmark;

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
        var headBounds = ScalpMapRenderer.DrawHead(drawingContext, ActualWidth, ActualHeight, 5, 24, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (headBounds.IsEmpty) return;

        foreach (var point in ElectrodeMapProjection.ProjectWithHardwareLandmarks(Rows ?? [], ReferencePosition, GroundPosition))
        {
            var position = ScalpMapRenderer.ToCanvas(point.X, point.Y, headBounds);
            var isManual = (Rows ?? []).FirstOrDefault(row => row.ElectrodeLabel == point.Label)?.ManualMapPosition is not null;
            ScalpMapRenderer.DrawElectrode(drawingContext, point.Label, position, point.IsHardwareLandmark, isManual, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pointer = e.GetPosition(this);
        draggedLandmark = HitTestHardwareLandmark(
            pointer, ActualWidth, ActualHeight, ReferencePosition, GroundPosition, LandmarkHitRadius);
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
        var center = new Point(ActualWidth / 2, ActualHeight / 2 + 5);
        var radius = Math.Min(ActualWidth, ActualHeight - 24) * 0.39;
        if (radius <= 0 || SelectedRow is not { } row || !CanPlaceRow(row))
        {
            return;
        }
        var x = Math.Clamp((pointer.X - center.X) / radius, -1, 1);
        var y = Math.Clamp((pointer.Y - center.Y) / radius, -1, 1);
        if (x * x + y * y <= 1)
        {
            row.SetManualMapPosition(new ElectrodeMapPosition(x, y));
            InvalidateVisual();
        }
    }

    private void MoveHardwareLandmark(Point pointer)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2 + 5);
        var radius = Math.Min(ActualWidth, ActualHeight - 24) * 0.39;
        if (radius <= 0)
        {
            return;
        }

        var position = new ElectrodeMapPosition(
            Math.Clamp((pointer.X - center.X) / radius, -1, 1),
            Math.Clamp((pointer.Y - center.Y) / radius, -1, 1));
        if (position.X * position.X + position.Y * position.Y > 1)
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
        var center = new Point(width / 2, height / 2 + 5);
        var radius = Math.Min(width, height - 24) * 0.39;
        if (radius <= 0 || hitRadius < 0)
        {
            return null;
        }

        var reference = ToCanvasPosition(referencePosition, center, radius);
        var ground = ToCanvasPosition(groundPosition, center, radius);
        var referenceDistance = (pointer - reference).Length;
        var groundDistance = (pointer - ground).Length;
        if (referenceDistance > hitRadius && groundDistance > hitRadius)
        {
            return null;
        }
        return referenceDistance <= groundDistance ? "REF" : "GND";
    }

    private static Point ToCanvasPosition(ElectrodeMapPosition position, Point center, double radius) =>
        new(center.X + position.X * radius, center.Y + position.Y * radius);

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
