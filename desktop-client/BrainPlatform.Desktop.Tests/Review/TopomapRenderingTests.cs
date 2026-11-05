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
        var band = CreateField("rbp.relative.alpha", "alpha", "ratio", grid);

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
        var band = CreateField("rbp.relative.alpha", "alpha", "ratio", grid);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => TopomapVisualPreparer.Prepare(
            band, 160, 160, cancellation.Token));
    }

    [Fact]
    public void Relative_scale_is_fixed_to_ratio_domain()
    {
        var state = new TopomapColorScaleState();
        var band = CreateBand("alpha", "ratio", 0.2, 0.7);

        Assert.Equal((0d, 1d), state.GetBounds(band));
    }

    [Fact]
    public void Psd_band_mean_density_scale_converts_SI_values_once_to_microvolts_squared_per_hertz()
    {
        var state = new TopomapColorScaleState();
        var field = CreateField("psd.band-mean-density.gamma", "Gamma 平均 PSD (30–50 Hz)", "V^2/Hz", CreateGrid(1e-12, 8e-12));

        state.Remember(field, 1e-12, 8e-12);

        Assert.Equal((1e-12, 8e-12), state.GetBounds(field));
        Assert.Equal("µV²/Hz", TopomapCanvas.DisplayUnit(field.Unit));
        Assert.Equal("1", TopomapCanvas.FormatScaleValue(1e-12, field.Unit));
        Assert.Equal("8", TopomapCanvas.FormatScaleValue(8e-12, field.Unit));
    }

    [Fact]
    public async Task Absolute_scale_is_locked_per_band_until_reset()
    {
        var state = new TopomapColorScaleState();

        var alpha = CreateBand("alpha", "uV^2", 2, 8);
        var laterAlpha = CreateBand("alpha", "uV^2", 1, 20);
        var alphaSnapshot = await Task.Run(() => TopomapVisualPreparer.Prepare(
            alpha, 160, 160, CancellationToken.None));
        var alphaBounds = (alphaSnapshot.Minimum, alphaSnapshot.Maximum);
        state.Remember(alpha, alphaBounds.Item1, alphaBounds.Item2);
        var laterAlphaBounds = state.GetBounds(laterAlpha);
        var beta = CreateBand("beta", "uV^2", 4, 12);
        var betaSnapshot = await Task.Run(() => TopomapVisualPreparer.Prepare(
            beta, 160, 160, CancellationToken.None));
        state.Remember(beta, betaSnapshot.Minimum, betaSnapshot.Maximum);
        var betaBounds = state.GetBounds(beta);

        Assert.Equal((2d, 8d), alphaBounds);
        Assert.Equal(alphaBounds, laterAlphaBounds);
        Assert.Equal((4d, 12d), betaBounds);

        state.Reset();
        Assert.Null(state.GetBounds(laterAlpha));
        state.Remember(laterAlpha, 1, 20);
        Assert.Equal((1d, 20d), state.GetBounds(laterAlpha));
    }

    [Fact]
    public void Prepared_absolute_scale_clamps_values_to_endpoint_colors()
    {
        var grid = CreateGrid(-2, 12);
        var pixels = TopomapVisualPreparer.BuildGridPixels(grid, 0, 10);

        Assert.Equal(TopomapVisualPreparer.ColorValue(0), ColorAt(pixels, 0));
        Assert.Equal(TopomapVisualPreparer.ColorValue(1), ColorAt(pixels, 2));
    }

    [Fact]
    public void Psd_density_uses_its_full_small_valued_range_for_colors()
    {
        const double minimum = 1.4665e-14;
        const double maximum = 2.2015e-13;
        var field = CreateField("psd.band-mean-density.gamma", "Gamma 波", "V^2/Hz",
            CreateGrid(minimum, maximum));
        var snapshot = TopomapVisualPreparer.Prepare(field, 180, 180, CancellationToken.None);
        var pixels = new byte[3 * 4];
        snapshot.GridBitmap!.CopyPixels(pixels, 3 * 4, 0);

        Assert.Equal(minimum, snapshot.Minimum);
        Assert.Equal(maximum, snapshot.Maximum);
        Assert.Equal(TopomapVisualPreparer.ColorValue(0), ColorAt(pixels, 0));
        Assert.Equal(TopomapVisualPreparer.ColorValue(0.5), ColorAt(pixels, 1));
        Assert.Equal(TopomapVisualPreparer.ColorValue(1), ColorAt(pixels, 2));
    }

    [Fact]
    public void Equivalent_psd_density_and_absolute_power_ranges_render_the_same_colors()
    {
        const double minimumDensity = 1.4665e-14;
        const double maximumDensity = 2.2015e-13;
        const double absolutePowerScale = 20e12;
        var densityGrid = CreateGrid(minimumDensity, maximumDensity);
        var powerGrid = CreateGrid(minimumDensity * absolutePowerScale,
            maximumDensity * absolutePowerScale);
        var densityPixels = TopomapVisualPreparer.BuildGridPixels(
            densityGrid, minimumDensity, maximumDensity);
        var powerPixels = TopomapVisualPreparer.BuildGridPixels(
            powerGrid, minimumDensity * absolutePowerScale, maximumDensity * absolutePowerScale);

        for (var index = 0; index < densityPixels.Length; index++)
            Assert.InRange(Math.Abs(densityPixels[index] - powerPixels[index]), 0, 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1e-13)]
    [InlineData(-1e-13)]
    [InlineData(2)]
    public void Constant_field_keeps_exact_scale_bounds_and_uses_midpoint_color(double value)
    {
        var field = CreateField("psd.band-mean-density.gamma", "Gamma 波", "V^2/Hz",
            CreateGrid(value, value));
        var snapshot = TopomapVisualPreparer.Prepare(
            field, 180, 180, CancellationToken.None);
        var pixels = new byte[3 * 4];
        snapshot.GridBitmap!.CopyPixels(pixels, 3 * 4, 0);

        Assert.Equal(value, snapshot.Minimum);
        Assert.Equal(value, snapshot.Maximum);
        Assert.All(Enumerable.Range(0, 3), index =>
            Assert.Equal(TopomapVisualPreparer.ColorValue(0.5), ColorAt(pixels, index)));
        Assert.True(snapshot.Contours!.IsEmpty());
    }

    [Fact]
    public void Constant_locked_bounds_saturate_later_lower_and_upper_values()
    {
        const double bound = 1e-13;
        var pixels = TopomapVisualPreparer.BuildGridPixels(CreateGrid(0, 2 * bound), bound, bound);

        Assert.Equal(TopomapVisualPreparer.ColorValue(0), ColorAt(pixels, 0));
        Assert.Equal(TopomapVisualPreparer.ColorValue(0.5), ColorAt(pixels, 1));
        Assert.Equal(TopomapVisualPreparer.ColorValue(1), ColorAt(pixels, 2));
    }

    [Fact]
    public void Prepared_visual_uses_locked_bounds_for_legend_and_contours()
    {
        var band = CreateBand("alpha", "uV^2", 1, 9);

        var snapshot = TopomapVisualPreparer.Prepare(band, 180, 180, CancellationToken.None, (0d, 10d));

        Assert.Equal(0, snapshot.Minimum);
        Assert.Equal(10, snapshot.Maximum);
        Assert.True(snapshot.HasGridValues);
    }

    private static TopomapField CreateBand(string id, string unit, double minimum, double maximum) =>
        CreateField($"rbp.absolute.{id}", id, unit, CreateGrid(minimum, maximum));

    private static TopomapField CreateField(string id, string label, string unit, TopomapGrid grid) =>
        new(id, label, unit, null, Array.Empty<TopomapPoint>(), grid, "ready",
            Array.Empty<string>(), Array.Empty<string>(), null, null);

    private static TopomapGrid CreateGrid(double minimum, double maximum) =>
        new("test", 3, 1, -1, 1, 0, 0, 1,
            new IReadOnlyList<double?>[] { new double?[] { minimum, (minimum + maximum) / 2, maximum } });

    private static System.Windows.Media.Color ColorAt(byte[] pixels, int column) =>
        System.Windows.Media.Color.FromRgb(pixels[column * 4 + 2], pixels[column * 4 + 1], pixels[column * 4]);
}
