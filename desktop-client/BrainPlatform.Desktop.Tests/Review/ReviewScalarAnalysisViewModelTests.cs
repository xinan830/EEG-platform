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
                        new ReviewScalarPoint(41.4, 41, 41.4, 9.18, "Complete", "clean", "", null, null),
                        new ReviewScalarPoint(42, 41.4, 42, 9.0, "Complete", "clean", "", null, null),
                    },
                };
                var axis = Assert.Single(Chart(iapf).YAxes);
                Assert.Equal(AutoRange.Never, axis.AutoRange);
                Assert.Equal(8d, Convert.ToDouble(axis.VisibleRange.Min));
                Assert.Equal(13d, Convert.ToDouble(axis.VisibleRange.Max));

                var thetaBeta = new ReviewScalarTrendChart { AlgorithmId = "theta_beta", Unit = "dimensionless" };
                Assert.Equal(AutoRange.Always, Assert.Single(Chart(thetaBeta).YAxes).AutoRange);
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
        var viewModel = new ReviewScalarAnalysisViewModel(unit, projector);

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

        viewModel.Apply(null, "F3", null);

        Assert.Equal("分析范围不可用", viewModel.CurrentWindowRangeText);
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
