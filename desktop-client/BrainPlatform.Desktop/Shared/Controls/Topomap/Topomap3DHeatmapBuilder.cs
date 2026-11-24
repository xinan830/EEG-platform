using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

public enum Topomap3DPresetKind
{
    OccipitalAlpha,
    FrontalActivation,
    HemisphericAsymmetry,
}

public enum Topomap3DColormapKind
{
    Turbo,
    Jet,
    RdBuReversed,
    Viridis,
}

/// <summary>
/// Builds smooth per-vertex UV coordinates (U = interpolated normalized value, V = anatomical scalp feather mask)
/// and 2D colormap textures (blending vivid colormap at V=0 to neutral head gray at V=1) so 3D topomaps render
/// with zero jagged triangle borders and zero color bleed onto the face, ears, or neck.
/// </summary>
internal static class Topomap3DHeatmapBuilder
{
    private static readonly Color NeutralScalpColor = Color.FromRgb(205, 211, 218);

    internal static PointCollection ComputeVertexUvCoordinates(
        Point3D[] vertices,
        IReadOnlyList<Topomap3DElectrodeMarker> markers,
        Topomap3DPresetKind preset,
        IReadOnlyDictionary<string, double>? explicitChannelValues = null)
    {
        var uvs = new PointCollection(vertices.Length);
        if (markers.Count == 0)
        {
            for (var i = 0; i < vertices.Length; i++) uvs.Add(new Point(0.5, 1.0));
            uvs.Freeze();
            return uvs;
        }

        var markerPositions = new Point3D[markers.Count];
        var markerValues = new double[markers.Count];
        var minElectrodeY = double.PositiveInfinity;
        var minFrontElectrodeY = double.PositiveInfinity;

        for (var i = 0; i < markers.Count; i++)
        {
            var pos = markers[i].Position;
            markerPositions[i] = pos;
            if (pos.Y < minElectrodeY) minElectrodeY = pos.Y;
            if (pos.Z < -0.12 && pos.Y < minFrontElectrodeY) minFrontElectrodeY = pos.Y;

            if (explicitChannelValues is not null &&
                (explicitChannelValues.TryGetValue(markers[i].DisplayLabel, out var rawVal) ||
                 explicitChannelValues.TryGetValue(markers[i].CanonicalChannel, out rawVal)))
            {
                markerValues[i] = Math.Clamp(rawVal, 0d, 1d);
            }
            else
            {
                markerValues[i] = EvaluatePresetAtPosition(pos, preset);
            }
        }

        if (!double.IsFinite(minFrontElectrodeY)) minFrontElectrodeY = minElectrodeY + 0.04;

        // Only auto-stretch to [0.04, 0.96] in preview preset mode; real EEG data preserves exact [vmin, vmax] normalization.
        if (explicitChannelValues is null)
        {
            var vMin = markerValues.Min();
            var vMax = markerValues.Max();
            var vSpan = vMax - vMin;
            if (vSpan > 1e-6)
            {
                for (var i = 0; i < markerValues.Length; i++)
                {
                    markerValues[i] = 0.04 + 0.92 * ((markerValues[i] - vMin) / vSpan);
                }
            }
        }

        const double sigma = 0.145;
        const double twoSigmaSq = 2d * sigma * sigma;
        const double sigmaSq = sigma * sigma;

        for (var idx = 0; idx < vertices.Length; idx++)
        {
            var p = vertices[idx];
            var weightedSum = 0d;
            var weightTotal = 0d;
            var minDistSq = double.PositiveInfinity;

            for (var m = 0; m < markerPositions.Length; m++)
            {
                var dx = p.X - markerPositions[m].X;
                var dy = p.Y - markerPositions[m].Y;
                var dz = p.Z - markerPositions[m].Z;
                var distSq = dx * dx + dy * dy + dz * dz;
                if (distSq < minDistSq) minDistSq = distSq;

                var w = Math.Exp(-distSq / twoSigmaSq) + 0.14 / Math.Pow(distSq + sigmaSq, 1.5);
                weightedSum += w * markerValues[m];
                weightTotal += w;
            }

            var u = weightTotal > 1e-12 ? Math.Clamp(weightedSum / weightTotal, 0.01, 0.99) : 0.5;
            var minDist = Math.Sqrt(minDistSq);

            // 1. Proximity mask to the active electrode array (smoothly feathers out beyond the outer ring)
            var proximityMask = 1d - SmoothStep(0.16, 0.29, minDist);

            // 2. Anatomical cranial cap mask (excludes eye sockets, nose, ears, and neck cleanly)
            var frontBlend = SmoothStep(-0.04, -0.24, p.Z); // 1 on forehead/face (-Z), 0 on back (+Z)
            var cutoffY = minElectrodeY - 0.035 + frontBlend * Math.Max(0.02, (minFrontElectrodeY - minElectrodeY) + 0.025);
            var heightMask = SmoothStep(cutoffY - 0.045, cutoffY + 0.045, p.Y);

            // 3. Ear exclusion (lateral protrusions around |X| > 0.36 and low Y)
            var earLateral = SmoothStep(0.33, 0.41, Math.Abs(p.X)) * (1d - SmoothStep(minElectrodeY - 0.02, minElectrodeY + 0.09, p.Y));
            var earMask = Math.Clamp(1d - earLateral, 0d, 1d);

            var scalpWeight = Math.Clamp(proximityMask * heightMask * earMask, 0d, 1d);
            var v = Math.Clamp(1d - scalpWeight, 0.01, 0.99);
            uvs.Add(new Point(u, v));
        }

        uvs.Freeze();
        return uvs;
    }

    internal static MaterialGroup CreateColormapMaterial(Topomap3DColormapKind colormap)
    {
        const int width = 256;
        const int height = 64;
        var pixels = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var grayBlend = y / (double)(height - 1); // 0 = pure colormap, 1 = neutral scalp gray
            for (var x = 0; x < width; x++)
            {
                var t = x / (double)(width - 1);
                var heat = EvaluateColormap(t, colormap);
                var r = (byte)Math.Round(heat.R * (1d - grayBlend) + NeutralScalpColor.R * grayBlend);
                var g = (byte)Math.Round(heat.G * (1d - grayBlend) + NeutralScalpColor.G * grayBlend);
                var b = (byte)Math.Round(heat.B * (1d - grayBlend) + NeutralScalpColor.B * grayBlend);

                var offset = (y * width + x) * 4;
                pixels[offset + 0] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        bitmap.Freeze();

        var brush = new ImageBrush(bitmap)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.None,
            Stretch = Stretch.Fill,
        };
        brush.Freeze();

        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(brush));
        var specBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));
        specBrush.Freeze();
        group.Children.Add(new SpecularMaterial(specBrush, 24));
        group.Freeze();
        return group;
    }

    internal static LinearGradientBrush CreateColorbarBrush(Topomap3DColormapKind colormap)
    {
        var stops = new GradientStopCollection();
        const int count = 16;
        for (var i = 0; i <= count; i++)
        {
            var t = i / (double)count;
            // Top of vertical bar (offset 0) = max value (t = 1), bottom (offset 1) = min value (t = 0)
            stops.Add(new GradientStop(EvaluateColormap(1d - t, colormap), t));
        }

        var brush = new LinearGradientBrush(stops, new Point(0.5, 0), new Point(0.5, 1));
        brush.Freeze();
        return brush;
    }

    private static double EvaluatePresetAtPosition(Point3D p, Topomap3DPresetKind preset)
    {
        // Note: in normalized head coordinates, +X = Right, +Y = Up, +Z = Posterior (Occipital), -Z = Anterior (Frontal)
        return preset switch
        {
            Topomap3DPresetKind.OccipitalAlpha =>
                0.88 * Gaussian(p, new Point3D(-0.11, 0.10, 0.34), 0.22) +
                0.95 * Gaussian(p, new Point3D(0.12, 0.10, 0.34), 0.22) +
                0.55 * Gaussian(p, new Point3D(0.0, 0.34, 0.16), 0.24) +
                0.15 * Gaussian(p, new Point3D(0.0, 0.16, -0.34), 0.28),

            Topomap3DPresetKind.FrontalActivation =>
                0.95 * Gaussian(p, new Point3D(0.0, 0.24, -0.34), 0.23) +
                0.78 * Gaussian(p, new Point3D(-0.18, 0.20, -0.26), 0.21) +
                0.82 * Gaussian(p, new Point3D(0.18, 0.20, -0.26), 0.21) +
                0.30 * Gaussian(p, new Point3D(0.0, 0.38, 0.02), 0.25),

            Topomap3DPresetKind.HemisphericAsymmetry =>
                0.95 * Gaussian(p, new Point3D(0.26, 0.26, -0.06), 0.22) +
                0.72 * Gaussian(p, new Point3D(0.22, 0.22, -0.24), 0.20) -
                0.45 * Gaussian(p, new Point3D(-0.26, 0.25, -0.04), 0.22) +
                0.35,

            _ => 0.5,
        };
    }

    private static double Gaussian(Point3D a, Point3D b, double sigma)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return Math.Exp(-(dx * dx + dy * dy + dz * dz) / (2d * sigma * sigma));
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        if (Math.Abs(edge1 - edge0) < 1e-9) return x >= edge1 ? 1d : 0d;
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0d, 1d);
        return t * t * (3d - 2d * t);
    }

    private static Color EvaluateColormap(double t, Topomap3DColormapKind kind)
    {
        var clamped = Math.Clamp(t, 0d, 1d);
        ReadOnlySpan<(double Pos, Color Col)> stops = kind switch
        {
            Topomap3DColormapKind.Jet =>
            [
                (0.00, Color.FromRgb(0, 0, 143)),
                (0.15, Color.FromRgb(0, 48, 255)),
                (0.36, Color.FromRgb(0, 225, 255)),
                (0.55, Color.FromRgb(128, 255, 112)),
                (0.74, Color.FromRgb(255, 224, 0)),
                (0.90, Color.FromRgb(255, 64, 0)),
                (1.00, Color.FromRgb(140, 0, 0)),
            ],
            Topomap3DColormapKind.RdBuReversed =>
            [
                (0.00, Color.FromRgb(13, 59, 138)),
                (0.25, Color.FromRgb(59, 130, 214)),
                (0.50, Color.FromRgb(244, 246, 249)),
                (0.75, Color.FromRgb(232, 108, 74)),
                (1.00, Color.FromRgb(153, 18, 32)),
            ],
            Topomap3DColormapKind.Viridis =>
            [
                (0.00, Color.FromRgb(68, 1, 84)),
                (0.25, Color.FromRgb(59, 82, 139)),
                (0.50, Color.FromRgb(33, 145, 140)),
                (0.75, Color.FromRgb(94, 201, 98)),
                (1.00, Color.FromRgb(253, 231, 37)),
            ],
            _ => // Turbo (smooth perceptual rainbow)
            [
                (0.00, Color.FromRgb(48, 18, 59)),
                (0.16, Color.FromRgb(65, 105, 225)),
                (0.34, Color.FromRgb(28, 203, 213)),
                (0.52, Color.FromRgb(118, 238, 88)),
                (0.70, Color.FromRgb(245, 206, 28)),
                (0.86, Color.FromRgb(242, 96, 22)),
                (1.00, Color.FromRgb(138, 14, 18)),
            ],
        };

        for (var i = 0; i < stops.Length - 1; i++)
        {
            var (p0, c0) = stops[i];
            var (p1, c1) = stops[i + 1];
            if (clamped >= p0 && clamped <= p1)
            {
                var localT = (clamped - p0) / Math.Max(1e-6, p1 - p0);
                return Color.FromRgb(
                    (byte)Math.Round(c0.R + (c1.R - c0.R) * localT),
                    (byte)Math.Round(c0.G + (c1.G - c0.G) * localT),
                    (byte)Math.Round(c0.B + (c1.B - c0.B) * localT));
            }
        }

        return stops[^1].Col;
    }
}
