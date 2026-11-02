using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Shared.Controls.Topomap;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class TopomapRenderingTests
{
    [Fact]
    public void Grid_pixels_are_opaque_for_interpolated_cells()
    {
        var grid = new TopomapGrid("test", 5, 5, -1, 1, -1, 1, 1,
            Enumerable.Range(0, 5).Select(row => Enumerable.Range(0, 5)
                .Select(column => row == 2 && column == 2 ? (double?)1d : null)
                .ToArray()).ToArray());

        var pixels = TopomapVisualPreparer.BuildGridPixels(grid, 0, 1);

        Assert.Equal(5 * 5 * 4, pixels.Length);
        Assert.All(Enumerable.Range(0, 25), index => Assert.Equal(255, pixels[index * 4 + 3]));
        Assert.Equal(190, pixels[(2 * 5 + 2) * 4 + 2]);
    }

    [Fact]
    public void Grid_pixels_leave_declared_outside_cells_transparent()
    {
        var grid = new TopomapGrid("test", 9, 9, -1, 1, -1, 1, 0.5,
            Enumerable.Range(0, 9).Select(row => Enumerable.Range(0, 9)
                .Select(column => row == 4 && column == 4 ? (double?)1d : null)
                .ToArray()).ToArray());

        var pixels = TopomapVisualPreparer.BuildGridPixels(grid, 0, 1);

        Assert.Equal(0, pixels[0 * 4 + 3]);
        Assert.Equal(255, pixels[(4 * 9 + 4) * 4 + 3]);
    }

    [Fact]
    public async Task Prepared_visual_resources_are_frozen_for_cross_thread_rendering()
    {
        var values = Enumerable.Range(0, 5)
            .Select(row => (IReadOnlyList<double?>)Enumerable.Range(0, 5)
                .Select(column => (double?)(row + column)).ToArray())
            .ToArray();
        var grid = new TopomapGrid("test", 5, 5, -1, 1, -1, 1, 1, values);
        var band = new TopomapBand("alpha", "ratio", Array.Empty<TopomapPoint>(), grid, "ready",
            Array.Empty<string>(), Array.Empty<string>(), null, null);

        var snapshot = await Task.Run(() => TopomapVisualPreparer.Prepare(
            band, 160, 160, CancellationToken.None));

        Assert.NotNull(snapshot.GridBitmap);
        Assert.True(snapshot.GridBitmap!.IsFrozen);
        Assert.True(snapshot.HeadClip!.IsFrozen);
        Assert.True(snapshot.Contours!.IsFrozen);
        Assert.True(snapshot.ContourPen!.IsFrozen);
        Assert.Equal(5, snapshot.GridBitmap.PixelWidth);
    }

    [Fact]
    public void Visual_preparation_honors_cancellation()
    {
        var grid = new TopomapGrid("test", 1, 1, 0, 0, 0, 0, 1,
            new IReadOnlyList<double?>[] { new double?[] { 1 } });
        var band = new TopomapBand("alpha", "ratio", Array.Empty<TopomapPoint>(), grid, "ready",
            Array.Empty<string>(), Array.Empty<string>(), null, null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => TopomapVisualPreparer.Prepare(
            band, 160, 160, cancellation.Token));
    }
}
