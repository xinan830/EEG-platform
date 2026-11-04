using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewRbpParametersTests
{
    [Fact]
    public void Configuration_uses_the_catalog_ten_second_default_window()
    {
        var recording = new RegisteredRecording("recording", "fixture", ".wpf", "now", 200, 40,
            ["F3-REF"], "sha");
        var algorithm = new AlgorithmCatalogItem("official", "rbp", "fixture-rbp-v1", "相对频段功率",
            "RBP", "", [], ["dynamic"], default, new DynamicAnalysisPolicy(4, [5, 10, 20, 30], 10, 1, true),
            "available", true, "rbp-definition", "1", "rbp-runtime-v1");
        var parameters = new ReviewRbpParameters();
        parameters.Initialize(recording, algorithm);

        using var config = JsonDocument.Parse(parameters.BuildConfig(40, 200).GetRawText());

        Assert.Equal(10, config.RootElement.GetProperty("dynamic_window_s").GetDouble());
        Assert.Equal(1, config.RootElement.GetProperty("refresh_step_s").GetDouble());
    }
}
