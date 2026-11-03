using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Windows.Media.Media3D;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Channels.Api;
using BrainPlatform.Desktop.Shared.Controls.Topomap;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class Topomap3DRenderingTests
{
    [Fact]
    public void Scene_is_frozen_and_declared_mesh_identity_is_checked()
    {
        OnSta(() =>
        {
            var presentation = CreatePresentation();
            var scene = Topomap3DScenePreparer.Prepare(presentation, "alpha", CancellationToken.None);
            Assert.True(scene.IsFrozen);
            Assert.Single(scene.Children);

            var invalid = presentation with
            {
                Result = presentation.Result with { MeshGeometrySha256 = new string('0', 64) },
            };
            Assert.Throws<InvalidDataException>(() =>
                Topomap3DScenePreparer.Prepare(invalid, "alpha", CancellationToken.None));
        });
    }

    [Fact]
    public void Model_only_scene_shows_selected_mapped_locations_and_centers_the_head()
    {
        OnSta(() =>
        {
            var model = CreatePresentation().Model with
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

    private static Topomap3DPresentation CreatePresentation()
    {
        var vertices = new IReadOnlyList<double>[]
        {
            new[] { -0.5, 0d, 0d }, new[] { 0.5, 0d, 0d }, new[] { 0d, 0.5, 0d },
        };
        var triangles = new IReadOnlyList<int>[] { new[] { 0, 1, 2 } };
        var geometryHash = GeometryHash(vertices, triangles);
        const string mappingHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string assetHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var model = new ElectrodeScalpModel3D(
            "model", "v1", "mapping", 4, mappingHash, assetHash, geometryHash,
            "colin27_1020", "1.13.2", "head", "m", "gnm_native", "GNM model units",
            "standard_template_visualization", "measured contour fractions; anatomical review required",
            new[] { 0d, 0d, 0d }, new[] { 1d, 0d, 0d }, new[] { 0d, 0d, -1d },
            new[] { 0d, 1d, 0d }, Array.Empty<ElectrodeLocation3D>(), vertices, triangles);
        var band = new Topomap3DBand("alpha", "ratio", "ready", new double?[] { 0, 0.5, 1 },
            Array.Empty<Topomap3DElectrode>(), Array.Empty<string>(), Array.Empty<Topomap3DExcludedChannel>(),
            0, 1, null);
        var result = new Topomap3DResponse(
            "run", "recording", 0, 5, "ready", "rbp", "rbp-v1", "build", "relative", "clean",
            "model", "v1", "mapping", 4, mappingHash, assetHash, geometryHash,
            "colin27_1020", "1.13.2", "head", "m", "gnm_native", "GNM model units",
            "gnm_upper_scalp_gnomonic_linear_v1", new Dictionary<string, System.Text.Json.JsonElement>(),
            "standard_template_visualization", "measured contour fractions; anatomical review required",
            new[] { band }, null, null);
        return new Topomap3DPresentation(model, result);
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
