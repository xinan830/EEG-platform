using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Tests.Algorithms;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewScalarAnalysisViewModelTests
{
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
        Assert.Equal("F3", viewModel.SourceText);
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
}
