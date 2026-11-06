using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using SciChart.Charting.Visuals.Annotations;

namespace BrainPlatform.Desktop.Modules.Review.Views;

public sealed partial class ReviewCustomFrequencyChart
{
    private enum DragTarget { None, Point, Low, High, Range }
    private DragTarget dragTarget;
    private double dragAnchorHz;
    private double dragLowHz;
    private double dragHighHz;

    private void OnSelectionMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (surface.GridLinesPanel is FrameworkElement plot &&
            BeginSelectionDrag(e.GetPosition(plot), e.OriginalSource as DependencyObject)) e.Handled = true;
    }

    private bool BeginSelectionDrag(Point position, DependencyObject? source)
    {
        EndSelectionDrag();
        var target = HitSelection(position, source);
        if (target == DragTarget.None || !surface.CaptureMouse()) return false;
        dragTarget = target;
        dragAnchorHz = xAxis.GetCurrentCoordinateCalculator().GetDataValue(position.X);
        dragLowHz = Model!.RangeLowHz;
        dragHighHz = Model.RangeHighHz;
        if (target == DragTarget.Point)
        {
            // Labels can be shifted to avoid overlap; grabbing them must not jump the cursor.
            if (HitCursor(pointCursor, position, source)) dragLowHz = Model.ActualFrequencyHz!.Value;
            else { dragLowHz = dragAnchorHz; ApplyDraggedFrequency(pointCursor, dragAnchorHz); }
        }
        surface.Cursor = Cursors.SizeWE;
        return true;
    }

    private void OnSelectionMouseMove(object sender, MouseEventArgs e)
    {
        if (surface.GridLinesPanel is not FrameworkElement plot) return;
        var position = e.GetPosition(plot);
        if (dragTarget == DragTarget.None)
        {
            surface.Cursor = HitSelection(position, e.OriginalSource as DependencyObject) == DragTarget.None
                ? null : Cursors.SizeWE;
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed) { EndSelectionDrag(); return; }
        MoveSelectionDrag(position);
        e.Handled = true;
    }

    private void MoveSelectionDrag(Point position)
    {
        if (dragTarget == DragTarget.None || Model is not { HasBins: true }) return;
        var delta = xAxis.GetCurrentCoordinateCalculator().GetDataValue(position.X) - dragAnchorHz;
        if (!double.IsFinite(delta)) return;
        switch (dragTarget)
        {
            case DragTarget.Point: ApplyDraggedFrequency(pointCursor, dragLowHz + delta); break;
            case DragTarget.Low: ApplyDraggedFrequency(lowCursor, dragLowHz + delta); break;
            case DragTarget.High: ApplyDraggedFrequency(highCursor, dragHighHz + delta); break;
            case DragTarget.Range: TranslateRange(delta); break;
        }
    }

    private void OnSelectionMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (dragTarget == DragTarget.None) return;
        if (surface.GridLinesPanel is FrameworkElement plot) MoveSelectionDrag(e.GetPosition(plot));
        EndSelectionDrag();
        e.Handled = true;
    }

    private void EndSelectionDrag()
    {
        dragTarget = DragTarget.None;
        surface.ClearValue(CursorProperty);
        if (surface.IsMouseCaptured) surface.ReleaseMouseCapture();
    }

    private DragTarget HitSelection(Point position, DependencyObject? source)
    {
        if (Model is not { HasBins: true } model ||
            surface.GridLinesPanel is not FrameworkElement { ActualWidth: > 0 } plot) return DragTarget.None;
        var inside = position.X >= 0 && position.X <= plot.ActualWidth &&
            position.Y >= 0 && position.Y <= plot.ActualHeight;
        if (model.Mode == ReviewCustomFrequencyMode.SingleFrequency)
            return inside || OwnsCursorSource(pointCursor, source) ? DragTarget.Point : DragTarget.None;
        // Labels/handles take priority over the fill, including displaced top labels.
        if (OwnsCursorSource(lowCursor, source)) return DragTarget.Low;
        if (OwnsCursorSource(highCursor, source)) return DragTarget.High;
        if (!inside) return DragTarget.None;
        var calculator = xAxis.GetCurrentCoordinateCalculator();
        var lowDistance = Math.Abs(position.X - calculator.GetCoordinate(model.RangeLowHz));
        var highDistance = Math.Abs(position.X - calculator.GetCoordinate(model.RangeHighHz));
        if (Math.Min(lowDistance, highDistance) <= 8)
            return lowDistance <= highDistance ? DragTarget.Low : DragTarget.High;
        var frequency = calculator.GetDataValue(position.X);
        return model.IsRangeValid && frequency > model.RangeLowHz && frequency < model.RangeHighHz
            ? DragTarget.Range : DragTarget.None;
    }

    private bool HitCursor(VerticalLineAnnotation cursor, Point position, DependencyObject? source) =>
        !cursor.IsHidden && (OwnsCursorSource(cursor, source) ||
            Math.Abs(position.X - xAxis.GetCurrentCoordinateCalculator().GetCoordinate(Convert.ToDouble(cursor.X1))) <= 8);

    private static bool OwnsCursorSource(VerticalLineAnnotation cursor, DependencyObject? source)
    {
        if (cursor.IsHidden) return false;
        for (var current = source; current is Visual; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, cursor) || cursor.AnnotationLabels.Any(label => ReferenceEquals(label, current))) return true;
        return false;
    }

    private void TranslateRange(double delta)
    {
        var model = Model!;
        var axis = model.FrequenciesHz;
        var width = dragHighHz - dragLowHz;
        var requestedLow = Math.Clamp(dragLowHz + delta, axis[0], axis[^1] - width);
        var bestDistance = double.PositiveInfinity;
        var high = 1;
        var bestLow = dragLowHz;
        var bestHigh = dragHighHz;
        // Keep Hz width and both endpoints on real bins, even for a nonuniform grid.
        for (var low = 0; low < axis.Count - 1; low++)
        {
            high = Math.Max(high, low + 1);
            while (high < axis.Count && axis[high] - axis[low] < width - 1e-9) high++;
            if (high == axis.Count) break;
            if (Math.Abs(axis[high] - axis[low] - width) > 1e-9) continue;
            var distance = Math.Abs(axis[low] - requestedLow);
            if (distance >= bestDistance - 1e-9) continue;
            bestDistance = distance;
            bestLow = axis[low];
            bestHigh = axis[high];
        }
        model.SetRange(bestLow, bestHigh);
    }
}
