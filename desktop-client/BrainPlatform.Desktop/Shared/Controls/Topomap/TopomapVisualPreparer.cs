using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Shared.Controls;

namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

internal sealed record TopomapVisualSnapshot(
    TopomapField Field,
    Rect HeadBounds,
    Geometry? HeadClip,
    double CanvasWidth,
    double CanvasHeight,
    BitmapSource? GridBitmap,
    Geometry? Contours,
    Pen? ContourPen,
    double Minimum,
    double Maximum,
    bool HasGridValues,
    double RasterMilliseconds,
    double ContourMilliseconds,
    double PreparationMilliseconds);

internal static class TopomapVisualPreparer
{
    private static readonly Color ContourColor = Color.FromArgb(150, 255, 255, 255);

    internal static TopomapVisualSnapshot Prepare(
        TopomapField band,
        double canvasWidth,
        double canvasHeight,
        CancellationToken cancellationToken,
        (double Minimum, double Maximum)? scaleBounds = null)
    {
        var timer = Stopwatch.StartNew();
        var bounds = ScalpMapRenderer.GetHeadBounds(canvasWidth, canvasHeight,
            ScalpMapRenderer.VerticalOffset, ScalpMapRenderer.ReservedHeight, ScalpMapRenderer.RadiusScale);
        if (bounds.IsEmpty || band.Grid is not { } grid || band.Status != "ready")
            return new TopomapVisualSnapshot(band, bounds, null, canvasWidth, canvasHeight,
                null, null, null, 0, 1, false, 0, 0, timer.Elapsed.TotalMilliseconds);

        double minimum;
        double maximum;
        bool hasValues;
        if (scaleBounds is { } fixedBounds)
        {
            (minimum, maximum) = fixedBounds;
            hasValues = HasGridValues(grid, cancellationToken);
        }
        else
        {
            (minimum, maximum, hasValues) = ValueRange(grid, cancellationToken);
        }
        var rasterTimer = Stopwatch.StartNew();
        var pixels = BuildGridPixels(grid, minimum, maximum, cancellationToken);
        var bitmap = BitmapSource.Create(Math.Max(1, grid.Width), Math.Max(1, grid.Height), 96, 96,
            PixelFormats.Bgra32, null, pixels, Math.Max(1, grid.Width) * 4);
        bitmap.Freeze();
        var rasterMilliseconds = rasterTimer.Elapsed.TotalMilliseconds;

        var contourTimer = Stopwatch.StartNew();
        var contours = BuildContours(grid, bounds, minimum, maximum, cancellationToken);
        var contourPen = new Pen(new SolidColorBrush(ContourColor), 0.7);
        contours.Freeze();
        contourPen.Freeze();
        var clip = new EllipseGeometry(
            new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2),
            bounds.Width / 2, bounds.Height / 2);
        clip.Freeze();
        var contourMilliseconds = contourTimer.Elapsed.TotalMilliseconds;

        return new TopomapVisualSnapshot(band, bounds, clip, canvasWidth, canvasHeight,
            bitmap, contours, contourPen, minimum, maximum, hasValues,
            rasterMilliseconds, contourMilliseconds, timer.Elapsed.TotalMilliseconds);
    }

    internal static byte[] BuildGridPixels(TopomapGrid grid, double minimum, double maximum,
        CancellationToken cancellationToken = default)
    {
        var width = Math.Max(1, grid.Width);
        var height = Math.Max(1, grid.Height);
        var pixels = new byte[width * height * 4];
        var samples = new List<(double X, double Y, double Value)>(width * height);
        for (var row = 0; row < Math.Min(height, grid.Values.Count); row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowValues = grid.Values[row];
            for (var column = 0; column < Math.Min(width, rowValues.Count); column++)
            {
                if (rowValues[column] is not { } value) continue;
                samples.Add((GridCoordinate(grid.XMin, grid.XMax, column, width),
                    GridCoordinate(grid.YMin, grid.YMax, row, height), value));
            }
        }

        var gridStepX = (grid.XMax - grid.XMin) / Math.Max(1, width - 1);
        var gridStepY = (grid.YMax - grid.YMin) / Math.Max(1, height - 1);
        var edgeRadius = grid.HeadRadius + Math.Sqrt(gridStepX * gridStepX + gridStepY * gridStepY);
        var edgeRadiusSquared = edgeRadius * edgeRadius;
        for (var row = 0; row < height; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var column = 0; column < width; column++)
            {
                var value = row < grid.Values.Count && column < grid.Values[row].Count
                    ? grid.Values[row][column]
                    : null;
                if (value is null)
                {
                    var gridX = GridCoordinate(grid.XMin, grid.XMax, column, width);
                    var gridY = GridCoordinate(grid.YMin, grid.YMax, row, height);
                    if (gridX * gridX + gridY * gridY > edgeRadiusSquared || samples.Count == 0)
                        continue;

                    var nearestDistance = double.PositiveInfinity;
                    var nearestValue = 0d;
                    foreach (var sample in samples)
                    {
                        var dx = sample.X - gridX;
                        var dy = sample.Y - gridY;
                        var distance = dx * dx + dy * dy;
                        if (distance >= nearestDistance) continue;
                        nearestDistance = distance;
                        nearestValue = sample.Value;
                    }
                    value = nearestValue;
                }

                var color = ColorValue(Normalize(value.Value, minimum, maximum));
                var pixel = (row * width + column) * 4;
                pixels[pixel] = color.B;
                pixels[pixel + 1] = color.G;
                pixels[pixel + 2] = color.R;
                pixels[pixel + 3] = 255;
            }
        }
        return pixels;
    }

    private static (double Minimum, double Maximum, bool HasValues) ValueRange(
        TopomapGrid grid, CancellationToken cancellationToken)
    {
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        foreach (var row in grid.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var value in row)
            {
                if (value is not { } finite) continue;
                minimum = Math.Min(minimum, finite);
                maximum = Math.Max(maximum, finite);
            }
        }
        return double.IsFinite(minimum) ? (minimum, maximum, true) : (0, 1, false);
    }

    private static bool HasGridValues(TopomapGrid grid, CancellationToken cancellationToken)
    {
        foreach (var row in grid.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.Any(value => value is { } finite && double.IsFinite(finite))) return true;
        }
        return false;
    }

    private static StreamGeometry BuildContours(TopomapGrid grid, Rect bounds,
        double minimum, double maximum, CancellationToken cancellationToken)
    {
        var geometry = new StreamGeometry();
        if (maximum <= minimum) return geometry;
        using var context = geometry.Open();
        var rowCount = Math.Min(grid.Height, grid.Values.Count);
        for (var index = 1; index <= 6; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var level = minimum + (maximum - minimum) * index / 7d;
            for (var row = 0; row < rowCount - 1; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var top = grid.Values[row];
                var bottom = grid.Values[row + 1];
                var columnCount = Math.Min(grid.Width, Math.Min(top.Count, bottom.Count));
                for (var column = 0; column < columnCount - 1; column++)
                {
                    var topLeftValue = top[column];
                    var topRightValue = top[column + 1];
                    var bottomRightValue = bottom[column + 1];
                    var bottomLeftValue = bottom[column];
                    if (topLeftValue is not { } topLeft || topRightValue is not { } topRight ||
                        bottomRightValue is not { } bottomRight || bottomLeftValue is not { } bottomLeft)
                        continue;

                    var topLeftPoint = GridPoint(grid, bounds, column, row);
                    var topRightPoint = GridPoint(grid, bounds, column + 1, row);
                    var bottomRightPoint = GridPoint(grid, bounds, column + 1, row + 1);
                    var bottomLeftPoint = GridPoint(grid, bounds, column, row + 1);
                    var crossings = 0;
                    var first = default(Point);
                    AddCrossing(context, topLeft, topRight, topLeftPoint, topRightPoint,
                        level, ref crossings, ref first);
                    AddCrossing(context, topRight, bottomRight, topRightPoint, bottomRightPoint,
                        level, ref crossings, ref first);
                    AddCrossing(context, bottomRight, bottomLeft, bottomRightPoint, bottomLeftPoint,
                        level, ref crossings, ref first);
                    AddCrossing(context, bottomLeft, topLeft, bottomLeftPoint, topLeftPoint,
                        level, ref crossings, ref first);
                }
            }
        }
        return geometry;
    }

    private static void AddCrossing(StreamGeometryContext context,
        double fromValue, double toValue, Point from, Point to, double level,
        ref int crossings, ref Point first)
    {
        if ((fromValue < level) == (toValue < level)) return;
        var ratio = (level - fromValue) / (toValue - fromValue);
        var point = from + (to - from) * ratio;
        if (crossings++ % 2 == 0)
        {
            first = point;
            return;
        }
        context.BeginFigure(first, isFilled: false, isClosed: false);
        context.LineTo(point, isStroked: true, isSmoothJoin: false);
    }

    private static Point GridPoint(TopomapGrid grid, Rect bounds, int column, int row) =>
        ScalpMapRenderer.ToCanvas(GridCoordinate(grid.XMin, grid.XMax, column, grid.Width),
            GridCoordinate(grid.YMin, grid.YMax, row, grid.Height), bounds);

    private static double GridCoordinate(double minimum, double maximum, int index, int count) =>
        minimum + (maximum - minimum) * index / Math.Max(1, count - 1);

    private static double Normalize(double value, double minimum, double maximum)
    {
        if (maximum <= minimum)
            return value < minimum ? 0 : value > maximum ? 1 : 0.5;
        return Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);
    }

    internal static Color ColorValue(double normalized)
    {
        normalized = Math.Clamp(normalized, 0, 1);
        if (normalized < 0.35)
            return Blend(normalized / 0.35, (36, 84, 180), (37, 170, 220));
        if (normalized < 0.65)
            return Blend((normalized - 0.35) / 0.30, (37, 170, 220), (246, 211, 77));
        return Blend((normalized - 0.65) / 0.35, (246, 211, 77), (190, 24, 38));
    }

    private static Color Blend(double ratio, (byte R, byte G, byte B) lower, (byte R, byte G, byte B) upper) =>
        Color.FromRgb((byte)(lower.R + (upper.R - lower.R) * ratio),
            (byte)(lower.G + (upper.G - lower.G) * ratio),
            (byte)(lower.B + (upper.B - lower.B) * ratio));
}
