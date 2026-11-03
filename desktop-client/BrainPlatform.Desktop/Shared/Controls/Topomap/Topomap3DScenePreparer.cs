using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Channels.Api;

namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

internal static class Topomap3DScenePreparer
{
    private static readonly ConcurrentDictionary<string, Point3D[]> VerticesByIdentity = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Int32Collection> FacesByIdentity = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, (Point3D[] Vertices, Vector3D[] Normals, double Scale, Point3D Center)> SceneGeometryByIdentity = new(StringComparer.Ordinal);

    internal static Model3DGroup PrepareElectrodeLocations(ElectrodeScalpModel3D model,
        IReadOnlyList<string> enabledLabels, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enabledLabels);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateModel(model);
        var vertices = VerticesByIdentity.GetOrAdd(model.MeshGeometrySha256, _ => ValidateAndReadVertices(model));
        var faces = FacesByIdentity.GetOrAdd(model.MeshGeometrySha256, _ => ValidateAndReadFaces(model, vertices.Length));
        if (vertices.Length == 0 || faces.Count == 0) throw new InvalidDataException("三维模型缺少网格几何。");
        var geometry = GetSceneGeometry(model, vertices, faces, cancellationToken);
        var scene = new Model3DGroup();
        var scalp = CreateScalpGeometry(geometry.Vertices, geometry.Normals, faces);
        AddModel(scene, scalp, Color.FromRgb(205, 211, 218));
        var selected = enabledLabels.Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var locations = model.ElectrodeLocations
            .Where(site => selected.Contains(site.Channel) || selected.Contains(site.CanonicalChannel))
            .GroupBy(site => site.CanonicalChannel, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().CoordinateGnm);
        var markers = BuildElectrodeMarkers(locations, model, geometry, cancellationToken);
        if (markers.TriangleIndices.Count > 0) AddModel(scene, markers, Color.FromRgb(22, 24, 27));
        scene.Freeze();
        return scene;
    }

    internal static (Point3D Position, Vector3D LookDirection, double NearPlane, double FarPlane)
        CalculateCameraFrame(Rect3D bounds, double viewportWidth, double viewportHeight,
            double fieldOfViewDegrees)
    {
        if (bounds.IsEmpty || !double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) ||
            viewportWidth <= 0 || viewportHeight <= 0 || !double.IsFinite(fieldOfViewDegrees) ||
            fieldOfViewDegrees is <= 0 or >= 180)
            throw new ArgumentOutOfRangeException(nameof(bounds), "三维视口或模型边界无效。");
        var target = new Point3D();
        var radius = BoundingSphereRadius(bounds, target);
        var verticalHalfAngle = fieldOfViewDegrees * Math.PI / 360;
        var horizontalHalfAngle = Math.Atan(Math.Tan(verticalHalfAngle) * viewportWidth / viewportHeight);
        var halfAngle = Math.Min(verticalHalfAngle, horizontalHalfAngle);
        var distance = radius / Math.Sin(halfAngle) * 1.12;
        var elevation = Math.Atan2(radius * 0.04, distance);
        var position = target + new Vector3D(0, distance * Math.Sin(elevation), -distance * Math.Cos(elevation));
        var nearestSurfaceDistance = (position - target).Length - radius;
        return (position, target - position, Math.Max(0.001, nearestSurfaceDistance * 0.5), distance + radius * 2);
    }

    private static double BoundingSphereRadius(Rect3D bounds, Point3D center)
    {
        var radiusSquared = 0d;
        for (var x = 0; x < 2; x++)
        for (var y = 0; y < 2; y++)
        for (var z = 0; z < 2; z++)
        {
            var corner = new Point3D(bounds.X + x * bounds.SizeX, bounds.Y + y * bounds.SizeY,
                bounds.Z + z * bounds.SizeZ);
            radiusSquared = Math.Max(radiusSquared, (corner - center).LengthSquared);
        }
        return Math.Max(Math.Sqrt(radiusSquared), 1e-6);
    }

    private static void ValidateModel(ElectrodeScalpModel3D model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.IntendedUse != "standard_template_visualization" ||
            model.SourceMontage != "colin27_1020" || model.SourceCoordinateFrame != "head" ||
            model.SourceCoordinateUnit != "m" || model.TargetCoordinateFrame != "gnm_native" ||
            model.TargetCoordinateUnit != "GNM model units" ||
            !IsUnitAxis(model.RightAxisGnm) || !IsUnitAxis(model.ForwardAxisGnm) || !IsUnitAxis(model.UpAxisGnm) ||
            !IsRightHandedHeadFrame(model.RightAxisGnm, model.ForwardAxisGnm, model.UpAxisGnm) ||
            Math.Abs(Dot(model.RightAxisGnm, model.ForwardAxisGnm)) > 1e-6 ||
            Math.Abs(Dot(model.RightAxisGnm, model.UpAxisGnm)) > 1e-6 ||
            Math.Abs(Dot(model.ForwardAxisGnm, model.UpAxisGnm)) > 1e-6 ||
            model.HeadOriginGnm.Count != 3 || model.HeadOriginGnm.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("三维模型头部坐标基准无效。");
    }

    private static bool IsRightHandedHeadFrame(IReadOnlyList<double> right,
        IReadOnlyList<double> forward, IReadOnlyList<double> up) =>
        (Vector3D.CrossProduct(new Vector3D(right[0], right[1], right[2]),
            new Vector3D(forward[0], forward[1], forward[2])) -
         new Vector3D(up[0], up[1], up[2])).Length <= 1e-6;

    private static (Point3D[] Vertices, Vector3D[] Normals, double Scale, Point3D Center) GetSceneGeometry(
        ElectrodeScalpModel3D model, Point3D[] vertices, Int32Collection faces, CancellationToken cancellationToken) =>
        SceneGeometryByIdentity.GetOrAdd(model.MappingSha256 + model.MeshGeometrySha256, _ =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transform = CreateTransform(model);
            var origin = new Point3D(model.HeadOriginGnm[0], model.HeadOriginGnm[1], model.HeadOriginGnm[2]);
            var sourcePoints = vertices.Select(point => transform.Transform(point - origin)).ToArray();
            var minX = sourcePoints.Min(point => point.X); var maxX = sourcePoints.Max(point => point.X);
            var minY = sourcePoints.Min(point => point.Y); var maxY = sourcePoints.Max(point => point.Y);
            var minZ = sourcePoints.Min(point => point.Z); var maxZ = sourcePoints.Max(point => point.Z);
            var extent = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            if (!double.IsFinite(extent) || extent <= 0) throw new InvalidDataException("三维模型范围无效。");
            var center = new Point3D((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
            // This only normalizes display size; it does not calibrate GNM coordinates.
            var scale = 1.25 / extent;
            var points = sourcePoints.Select(point => new Point3D(
                (point.X - center.X) * scale, (point.Y - center.Y) * scale, (point.Z - center.Z) * scale)).ToArray();
            return (points, CalculateNormals(points, faces, cancellationToken), scale, center);
        });

    private static Matrix3D CreateTransform(ElectrodeScalpModel3D model) => new(
        model.RightAxisGnm[0], model.UpAxisGnm[0], -model.ForwardAxisGnm[0], 0,
        model.RightAxisGnm[1], model.UpAxisGnm[1], -model.ForwardAxisGnm[1], 0,
        model.RightAxisGnm[2], model.UpAxisGnm[2], -model.ForwardAxisGnm[2], 0,
        0, 0, 0, 1);

    private static bool IsUnitAxis(IReadOnlyList<double> axis) => axis.Count == 3 &&
        axis.All(double.IsFinite) && Math.Abs(Math.Sqrt(axis.Sum(value => value * value)) - 1) < 1e-6;

    private static double Dot(IReadOnlyList<double> left, IReadOnlyList<double> right) =>
        left.Zip(right, (a, b) => a * b).Sum();

    private static Point3D[] ValidateAndReadVertices(ElectrodeScalpModel3D model)
    {
        var points = new Point3D[model.VerticesGnm.Count];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bytes = new byte[8];
        for (var index = 0; index < points.Length; index++)
        {
            var coordinates = model.VerticesGnm[index];
            if (coordinates.Count != 3 || coordinates.Any(value => !double.IsFinite(value)))
                throw new InvalidDataException("三维模型顶点坐标无效。");
            points[index] = new Point3D(coordinates[0], coordinates[1], coordinates[2]);
            foreach (var coordinate in coordinates)
            {
                BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(coordinate));
                hash.AppendData(bytes);
            }
        }
        foreach (var triangle in model.Triangles)
        {
            if (triangle.Count != 3 || triangle.Any(index => index < 0 || index >= points.Length))
                throw new InvalidDataException("三维模型面索引无效。");
            foreach (var index in triangle)
            {
                BinaryPrimitives.WriteInt64LittleEndian(bytes, index);
                hash.AppendData(bytes);
            }
        }
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(model.MeshGeometrySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("三维模型几何哈希校验失败。");
        return points;
    }

    private static Int32Collection ValidateAndReadFaces(ElectrodeScalpModel3D model, int vertexCount)
    {
        var result = new Int32Collection(model.Triangles.Count * 3);
        foreach (var triangle in model.Triangles)
        {
            if (triangle.Count != 3 || triangle.Any(index => index < 0 || index >= vertexCount))
                throw new InvalidDataException("三维模型面索引无效。");
            result.Add(triangle[0]); result.Add(triangle[1]); result.Add(triangle[2]);
        }
        result.Freeze();
        return result;
    }

    private static Vector3D[] CalculateNormals(Point3D[] points, Int32Collection faces,
        CancellationToken cancellationToken)
    {
        var normals = new Vector3D[points.Length];
        for (var index = 0; index < faces.Count; index += 3)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = faces[index]; var b = faces[index + 1]; var c = faces[index + 2];
            var normal = Vector3D.CrossProduct(points[b] - points[a], points[c] - points[a]);
            normals[a] += normal; normals[b] += normal; normals[c] += normal;
        }
        for (var index = 0; index < normals.Length; index++)
            if (normals[index].LengthSquared > 0) normals[index].Normalize();
        return normals;
    }

    private static MeshGeometry3D BuildElectrodeMarkers(IEnumerable<IReadOnlyList<double>> locations,
        ElectrodeScalpModel3D model,
        (Point3D[] Vertices, Vector3D[] Normals, double Scale, Point3D Center) geometry,
        CancellationToken cancellationToken)
    {
        var mesh = new MeshGeometry3D();
        foreach (var coordinates in locations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var center = MapElectrodePosition(coordinates, model, geometry);
            AddSphere(mesh, center, 0.0014 * geometry.Scale);
        }
        return mesh;
    }

    internal static Point3D MapElectrodePosition(IReadOnlyList<double> coordinates,
        ElectrodeScalpModel3D model,
        (Point3D[] Vertices, Vector3D[] Normals, double Scale, Point3D Center) geometry)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        if (coordinates.Count != 3 || coordinates.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("三维电极坐标无效。");

        var source = new Point3D(coordinates[0], coordinates[1], coordinates[2]);
        var origin = new Point3D(model.HeadOriginGnm[0], model.HeadOriginGnm[1], model.HeadOriginGnm[2]);
        var transformed = CreateTransform(model).Transform(source - origin);
        return new Point3D(
            (transformed.X - geometry.Center.X) * geometry.Scale,
            (transformed.Y - geometry.Center.Y) * geometry.Scale,
            (transformed.Z - geometry.Center.Z) * geometry.Scale);
    }

    private static MeshGeometry3D CreateScalpGeometry(Point3D[] points, Vector3D[] normals, Int32Collection faces)
    {
        var geometry = new MeshGeometry3D();
        foreach (var point in points) geometry.Positions.Add(point);
        foreach (var normal in normals) geometry.Normals.Add(normal);
        foreach (var index in faces) geometry.TriangleIndices.Add(index);
        return geometry;
    }

    private static void AddSphere(MeshGeometry3D mesh, Point3D center, double radius)
    {
        const int longitudeCount = 8;
        const int latitudeCount = 5;
        var start = mesh.Positions.Count;
        for (var latitude = 0; latitude <= latitudeCount; latitude++)
        {
            var phi = Math.PI * latitude / latitudeCount;
            for (var longitude = 0; longitude <= longitudeCount; longitude++)
            {
                var theta = 2 * Math.PI * longitude / longitudeCount;
                var normal = new Vector3D(Math.Sin(phi) * Math.Cos(theta), Math.Cos(phi), Math.Sin(phi) * Math.Sin(theta));
                mesh.Positions.Add(center + normal * radius);
                mesh.Normals.Add(normal);
            }
        }
        for (var latitude = 0; latitude < latitudeCount; latitude++)
        for (var longitude = 0; longitude < longitudeCount; longitude++)
        {
            var a = start + latitude * (longitudeCount + 1) + longitude;
            var b = a + longitudeCount + 1;
            mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(a + 1);
            mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1);
        }
    }

    private static void AddModel(Model3DGroup scene, MeshGeometry3D geometry, Color color)
    {
        if (geometry.TriangleIndices.Count == 0) return;
        geometry.Freeze();
        var brush = new SolidColorBrush(color); brush.Freeze();
        var material = new DiffuseMaterial(brush); material.Freeze();
        scene.Children.Add(new GeometryModel3D(geometry, material) { BackMaterial = material });
    }

}
