using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Windows.Media.Media3D;
using BrainPlatform.Desktop.Modules.Channels.Api;
using BrainPlatform.Desktop.Shared.Controls.Topomap;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class Topomap3DRenderingTests
{
    [Fact]
    public void Channel_settings_scene_shows_selected_mapped_locations_and_centers_the_head()
    {
        OnSta(() =>
        {
            var model = CreateModel() with
            {
                ElectrodeLocations =
                [
                    new ElectrodeLocation3D("Fp1", "Fp1", [0, 0.5, 0], "test", false, true),
                    new ElectrodeLocation3D("M1", "M1", [-0.1, 0.2, 0], "test", false, false),
                    new ElectrodeLocation3D("M2", "M2", [0.1, 0.2, 0], "test", false, false),
                ],
            };
            var scene = Topomap3DScenePreparer.PrepareElectrodeLocations(model, ["Fp1"], CancellationToken.None);
            var bounds = ((GeometryModel3D)scene.Children[0]).Bounds;

            Assert.True(scene.IsFrozen);
            Assert.Equal(2, scene.Children.Count);
            Assert.InRange(Math.Abs(bounds.X + bounds.SizeX / 2), 0, 1e-8);
            Assert.InRange(Math.Abs(bounds.Y + bounds.SizeY / 2), 0, 1e-8);
            Assert.InRange(Math.Abs(bounds.Z + bounds.SizeZ / 2), 0, 1e-8);

            var mastoidScene = Topomap3DScenePreparer.PrepareElectrodeLocations(model, ["M1", "M2"], CancellationToken.None);
            var mastoidMarkers = (MeshGeometry3D)((GeometryModel3D)mastoidScene.Children[1]).Geometry;
            Assert.Equal(480, mastoidMarkers.TriangleIndices.Count);

            var noMappedSites = Topomap3DScenePreparer.PrepareElectrodeLocations(model, ["REF"], CancellationToken.None);
            Assert.Single(noMappedSites.Children);
        });
    }

    [Fact]
    public void Camera_frame_targets_the_rotation_origin_and_encloses_asymmetric_bounds()
    {
        var bounds = new Rect3D(-0.3, -0.2, -0.5, 1.2, 0.9, 0.8);
        var landscape = Topomap3DScenePreparer.CalculateCameraFrame(bounds, 900, 600, 42);
        var portrait = Topomap3DScenePreparer.CalculateCameraFrame(bounds, 400, 800, 42);

        Assert.True(portrait.Position.Z < landscape.Position.Z);
        var radius = Math.Sqrt(0.9 * 0.9 + 0.7 * 0.7 + 0.5 * 0.5);
        var target = landscape.Position + landscape.LookDirection;
        var cameraDistance = Math.Sqrt(landscape.Position.X * landscape.Position.X +
            landscape.Position.Y * landscape.Position.Y + landscape.Position.Z * landscape.Position.Z);
        Assert.Equal(0, target.X, 10);
        Assert.Equal(0, target.Y, 10);
        Assert.Equal(0, target.Z, 10);
        Assert.True(cameraDistance > radius);
        Assert.True(landscape.NearPlane < cameraDistance - radius);
    }

    [Fact]
    public void Repeated_optical_zoom_is_bounded_without_moving_the_camera()
    {
        var fieldOfView = 42d;
        for (var index = 0; index < 500; index++)
            fieldOfView = Topomap3DView.CalculateZoomFieldOfView(fieldOfView, 120);
        Assert.Equal(2, fieldOfView);

        for (var index = 0; index < 500; index++)
            fieldOfView = Topomap3DView.CalculateZoomFieldOfView(fieldOfView, -120);
        Assert.Equal(42, fieldOfView);
    }

    [Fact]
    public void Shift_pan_maps_drag_delta_to_camera_plane()
    {
        var offset = Topomap3DView.CalculatePanOffset(10, 20, 100, 42,
            new Vector3D(0, 0, 2), new Vector3D(0, 1, 0));
        var unitsPerPixel = 4 * Math.Tan(42 * Math.PI / 360) / 100;

        Assert.Equal(-10 * unitsPerPixel, offset.X, 10);
        Assert.Equal(20 * unitsPerPixel, offset.Y, 10);
        Assert.Equal(0, offset.Z, 10);
        Assert.Equal(default, Topomap3DView.CalculatePanOffset(10, 20, 0, 42,
            new Vector3D(0, 0, 2), new Vector3D(0, 1, 0)));
    }

    private static ElectrodeScalpModel3D CreateModel()
    {
        var vertices = new IReadOnlyList<double>[]
        {
            new[] { -0.5, 0d, 0d }, new[] { 0.5, 0d, 0d },
            new[] { 0d, 0.5, 0d }, new[] { 0d, -0.5, 0d },
        };
        var triangles = new IReadOnlyList<int>[] { new[] { 0, 1, 2 }, new[] { 0, 3, 1 } };
        var geometryHash = GeometryHash(vertices, triangles);
        const string mappingHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string assetHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        return new ElectrodeScalpModel3D(
            "model", "v1", "mapping", 4, mappingHash, assetHash, geometryHash,
            "colin27_1020", "1.13.2", "head", "m", "gnm_native", "GNM model units",
            "standard_template_visualization", "measured contour fractions; anatomical review required",
            new[] { 0d, 0d, 0d }, new[] { 1d, 0d, 0d }, new[] { 0d, 0d, -1d },
            new[] { 0d, 1d, 0d }, Array.Empty<ElectrodeLocation3D>(), vertices, triangles);
    }

    private static string GeometryHash(IReadOnlyList<IReadOnlyList<double>> vertices,
        IReadOnlyList<IReadOnlyList<int>> triangles)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var number = new byte[8];
        foreach (var vertex in vertices)
        foreach (var coordinate in vertex)
        {
            BinaryPrimitives.WriteInt64LittleEndian(number, BitConverter.DoubleToInt64Bits(coordinate));
            hash.AppendData(number);
        }
        foreach (var triangle in triangles)
        foreach (var index in triangle)
        {
            BinaryPrimitives.WriteInt64LittleEndian(number, index);
            hash.AppendData(number);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
