using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace BrainPlatform.Desktop.Modules.Events.Services;

/// <summary>
/// Manages local user-uploaded event icons (PNG/JPG/BMP/ICO/SVG) and loads
/// them as frozen WPF ImageSource instances. Unreadable or missing icons return
/// null so callers can cleanly fall back to the event's configured color.
/// </summary>
public static class EventIconAssetService
{
    private static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".ico", ".svg"];
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string IconDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BrainPlatform",
        "event-icons");

    public const string FileDialogFilter = "图标文件 (*.png;*.svg;*.jpg;*.jpeg;*.bmp;*.ico)|*.png;*.svg;*.jpg;*.jpeg;*.bmp;*.ico";

    public static string ImportIcon(string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("未找到所选图标文件。", sourceFilePath);
        }

        var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("仅支持 PNG、SVG、JPG、BMP、ICO 格式的图标文件。");
        }

        Directory.CreateDirectory(IconDirectory);
        var destinationName = $"{Guid.NewGuid():N}{extension}";
        var destinationPath = Path.Combine(IconDirectory, destinationName);
        File.Copy(sourceFilePath, destinationPath, overwrite: true);

        if (LoadCore(destinationPath) is null)
        {
            try { File.Delete(destinationPath); } catch (IOException) { }
            throw new InvalidOperationException("图标文件无法解析，请更换有效的 PNG 或 SVG 图标。");
        }

        Cache.TryRemove(destinationPath, out _);
        return destinationPath;
    }

    public static ImageSource? TryLoadImage(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return null;
        }

        return Cache.GetOrAdd(iconPath, LoadCore);
    }

    private static ImageSource? LoadCore(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            if (string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase))
            {
                return LoadSvgAsDrawingImage(path);
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? LoadSvgAsDrawingImage(string path)
    {
        var doc = XDocument.Load(path);
        if (doc.Root is null)
        {
            return null;
        }

        var group = new DrawingGroup();
        foreach (var element in doc.Root.Descendants())
        {
            var localName = element.Name.LocalName.ToLowerInvariant();
            Geometry? geometry = localName switch
            {
                "path" => ParsePathGeometry(element.Attribute("d")?.Value),
                "rect" => ParseRectGeometry(element),
                "circle" => ParseCircleGeometry(element),
                "ellipse" => ParseEllipseGeometry(element),
                "line" => ParseLineGeometry(element),
                "polygon" => ParsePointsGeometry(element.Attribute("points")?.Value, closed: true),
                "polyline" => ParsePointsGeometry(element.Attribute("points")?.Value, closed: false),
                _ => null,
            };

            if (geometry is null)
            {
                continue;
            }

            var fillAttr = ResolveStyleAttribute(element, "fill");
            var strokeAttr = ResolveStyleAttribute(element, "stroke");
            var strokeWidth = ParseDouble(ResolveStyleAttribute(element, "stroke-width"), 1.5);

            var fillBrush = ParseBrush(fillAttr, defaultBrush: strokeAttr is null ? Brushes.SteelBlue : null);
            var strokeBrush = ParseBrush(strokeAttr, defaultBrush: null);
            var pen = strokeBrush is not null ? new Pen(strokeBrush, Math.Max(0.5, strokeWidth)) : null;
            pen?.Freeze();

            if (fillBrush is null && pen is null)
            {
                continue;
            }

            geometry.Freeze();
            var drawing = new GeometryDrawing(fillBrush, pen, geometry);
            drawing.Freeze();
            group.Children.Add(drawing);
        }

        if (group.Children.Count == 0)
        {
            return null;
        }

        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static Geometry? ParsePathGeometry(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;
        try { return Geometry.Parse(data); }
        catch { return null; }
    }

    private static Geometry? ParseRectGeometry(XElement el)
    {
        var w = ParseDouble(el.Attribute("width")?.Value, 0);
        var h = ParseDouble(el.Attribute("height")?.Value, 0);
        if (w <= 0 || h <= 0) return null;
        var x = ParseDouble(el.Attribute("x")?.Value, 0);
        var y = ParseDouble(el.Attribute("y")?.Value, 0);
        var rx = ParseDouble(el.Attribute("rx")?.Value, 0);
        var ry = ParseDouble(el.Attribute("ry")?.Value, rx);
        return new RectangleGeometry(new Rect(x, y, w, h), rx, ry);
    }

    private static Geometry? ParseCircleGeometry(XElement el)
    {
        var r = ParseDouble(el.Attribute("r")?.Value, 0);
        if (r <= 0) return null;
        return new EllipseGeometry(new Point(ParseDouble(el.Attribute("cx")?.Value, 0), ParseDouble(el.Attribute("cy")?.Value, 0)), r, r);
    }

    private static Geometry? ParseEllipseGeometry(XElement el)
    {
        var rx = ParseDouble(el.Attribute("rx")?.Value, 0);
        var ry = ParseDouble(el.Attribute("ry")?.Value, 0);
        if (rx <= 0 || ry <= 0) return null;
        return new EllipseGeometry(new Point(ParseDouble(el.Attribute("cx")?.Value, 0), ParseDouble(el.Attribute("cy")?.Value, 0)), rx, ry);
    }

    private static Geometry ParseLineGeometry(XElement el) =>
        new LineGeometry(
            new Point(ParseDouble(el.Attribute("x1")?.Value, 0), ParseDouble(el.Attribute("y1")?.Value, 0)),
            new Point(ParseDouble(el.Attribute("x2")?.Value, 0), ParseDouble(el.Attribute("y2")?.Value, 0)));

    private static Geometry? ParsePointsGeometry(string? raw, bool closed)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var tokens = raw.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 4 || tokens.Length % 2 != 0) return null;
        var points = new List<Point>(tokens.Length / 2);
        for (var i = 0; i < tokens.Length; i += 2)
        {
            if (!double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                return null;
            points.Add(new Point(x, y));
        }

        var figure = new PathFigure(points[0], [new PolyLineSegment(points.Skip(1), true)], closed);
        return new PathGeometry([figure]);
    }

    private static string? ResolveStyleAttribute(XElement el, string name)
    {
        var direct = el.Attribute(name)?.Value;
        if (!string.IsNullOrWhiteSpace(direct)) return direct.Trim();
        var style = el.Attribute("style")?.Value;
        if (string.IsNullOrWhiteSpace(style)) return null;
        foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf(':');
            if (idx > 0 && part[..idx].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return part[(idx + 1)..].Trim();
            }
        }
        return null;
    }

    private static Brush? ParseBrush(string? raw, Brush? defaultBrush)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultBrush;
        if (raw.Equals("none", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            return null;
        if (raw.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(raw);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return defaultBrush;
        }
    }

    private static double ParseDouble(string? raw, double fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var cleaned = raw.Trim().TrimEnd('p', 'x', '%');
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}
