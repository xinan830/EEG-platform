using System.Runtime.ExceptionServices;
using System.Windows;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Tests.Algorithms;
using BrainPlatform.Desktop.Modules.Review.Views;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewScalarAnalysisViewModelTests
{
    [Fact]
    public void ScalarTrendChart_FixesOnlyIapfAxisToAlphaRange()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var iapf = new ReviewScalarTrendChart
                {
                    AlgorithmId = "iapf",
                    Unit = "Hz",
                    Points = new[]
                    {
                        new ReviewScalarPoint(41.4, 41, 41.4, 9.18, "Complete", "clean", "", null, null, "peak", 9.18, 9.45),
                        new ReviewScalarPoint(42, 22, 42, 7.5, "Complete", "clean", "", null, null, "cog", null, 7.5),
                    },
                };
                var iapfSurface = Chart(iapf);
                var axis = Assert.Single(iapfSurface.YAxes);
                Assert.Equal(AutoRange.Never, axis.AutoRange);
                Assert.False(axis.DrawMinorTicks);
                Assert.Equal(8d, Convert.ToDouble(axis.VisibleRange.Min));
                Assert.Equal(13d, Convert.ToDouble(axis.VisibleRange.Max));
                var band = Assert.IsType<SciChart.Charting.Visuals.Annotations.BoxAnnotation>(Assert.Single(iapfSurface.Annotations));
                Assert.False(band.IsHidden);
                Assert.True(iapfSurface.RenderableSeries[0].IsVisible);
                Assert.Equal(2, iapfSurface.RenderableSeries[0].DataSeries.Count);
                Assert.Equal(7.5, Convert.ToDouble(iapfSurface.RenderableSeries[0].DataSeries.YValues[1]));
                Assert.False(iapfSurface.RenderableSeries[1].IsVisible);

                iapf.IapfDisplayMode = "compare";
                Assert.True(iapfSurface.RenderableSeries[0].IsVisible);
                Assert.False(iapfSurface.RenderableSeries[1].IsVisible);
                Assert.True(iapfSurface.RenderableSeries[2].IsVisible);
                Assert.True(iapfSurface.RenderableSeries[3].IsVisible);

                iapf.IapfDisplayMode = "peak";
                Assert.True(iapfSurface.RenderableSeries[0].IsVisible);
                Assert.True(iapfSurface.RenderableSeries[1].IsVisible);
                Assert.Equal(2, iapfSurface.RenderableSeries[1].DataSeries.Count);
                Assert.True(double.IsNaN(Convert.ToDouble(iapfSurface.RenderableSeries[1].DataSeries.YValues[1])));
                Assert.Equal(1, iapfSurface.RenderableSeries[2].DataSeries.Count);
                Assert.Equal(1, iapfSurface.RenderableSeries[3].DataSeries.Count);

                var thetaBeta = new ReviewScalarTrendChart { AlgorithmId = "theta_beta", Unit = "dimensionless" };
                var tbSurface = Chart(thetaBeta);
                Assert.Equal(AutoRange.Always, Assert.Single(tbSurface.YAxes).AutoRange);
                Assert.True(Assert.IsType<SciChart.Charting.Visuals.Annotations.BoxAnnotation>(Assert.Single(tbSurface.Annotations)).IsHidden);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Theory]
    [InlineData("iapf", "Hz")]
    [InlineData("theta_beta", "dimensionless")]
    public void Projects_backend_scalar_value_and_unit_without_recomputation(string algorithmId, string unit)
    {
        var request = ScalarWindowTestSupport.Request(algorithmId);
        var raw = ScalarWindowTestSupport.Response(request);
        var response = Preview(raw);
        Func<DynamicWindowResponse, string?, string?, IReadOnlyList<ReviewScalarPoint>> projector =
            algorithmId == "iapf" ? ReviewScalarProjectors.Iapf : ReviewScalarProjectors.ThetaBeta;
        var viewModel = new ReviewScalarAnalysisViewModel(unit, projector, algorithmId);

        viewModel.Apply(response, "F3", null);

        var point = Assert.Single(viewModel.Points);
        Assert.Equal(6, point.TimeSeconds);
        Assert.Equal(1, point.StartSeconds);
        Assert.Equal(6, point.EndSeconds);
        Assert.Equal(0.5, point.Value);
        Assert.Equal("Complete", point.State);
        Assert.NotNull(point.Evidence);
        Assert.NotNull(point.Provenance);
        Assert.Equal("1–6 s", viewModel.CurrentWindowRangeText);
        if (algorithmId == "iapf")
        {
            Assert.Equal("cog", viewModel.IapfDisplayMode);
            Assert.Equal("COG 9.9 Hz", viewModel.LatestValueText);
            viewModel.IapfDisplayMode = "peak";
            Assert.Equal("0.5 Hz", viewModel.LatestValueText);
        }
        else
        {
            Assert.Equal("0.5", viewModel.LatestValueText);
        }

        viewModel.Apply(null, "F3", null);

        Assert.Equal("分析范围不可用", viewModel.CurrentWindowRangeText);
        Assert.Equal("--", viewModel.LatestValueText);
    }

    [Fact]
    public void Invalid_backend_unit_clears_scalar_history_instead_of_displaying_a_value()
    {
        var raw = ScalarWindowTestSupport.Response(ScalarWindowTestSupport.Request("iapf"));
        var invalid = raw with
        {
            Result = raw.Result! with { ArrayUnits = new Dictionary<string, string> { ["iapf_hz"] = "dimensionless" } },
        };
        var viewModel = new ReviewScalarAnalysisViewModel("Hz", ReviewScalarProjectors.Iapf);

        viewModel.Apply(Preview(raw), "F3", null);
        viewModel.Apply(Preview(invalid), "F3", null);

        Assert.Empty(viewModel.Points);
        Assert.Contains("无法展示", viewModel.StatusText);
    }

    private static WindowPreviewResponse Preview(DynamicWindowResponse raw) => new(
        raw.SessionId, "recording", raw.Range!.EndSample, raw.Range.EndSeconds, "available", [],
        new WindowPreviewWindow(raw.WindowId, 0, raw.Range.StartSample, raw.Range.EndSample,
            "Complete", "clean", []), false, null, null, null, null, null, null,
        null, null)
    {
        DynamicResponse = raw,
    };

    private static SciChartSurface Chart(ReviewScalarTrendChart chart) =>
        Assert.IsType<SciChartSurface>(((System.Windows.Controls.Grid)chart.Content).Children[0]);
}
