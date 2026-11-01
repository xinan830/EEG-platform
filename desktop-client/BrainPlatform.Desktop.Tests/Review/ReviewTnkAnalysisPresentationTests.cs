using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.ViewModels;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewTnkAnalysisPresentationTests
{
    [Fact]
    public void Summary_and_charts_start_at_the_panel_open_cursor()
    {
        var model = CreateModel();
        model.SetVisibleStart(30);
        model.Apply(Window(29, 40), 30);
        model.Apply(Window(30, 60), 30);
        model.Apply(Window(31, 80), 30);
        model.SetCursor(31);

        var points = Assert.Single(Assert.Single(model.ActivePlots).Chart.Series).Points;
        Assert.Equal(new[] { 30d, 31d }, points.Select(point => point.X));
        Assert.Equal("80", model.CompositeValue);
        Assert.Equal(80, model.CompositeProgress);
        Assert.Equal(1, model.ScoreStates.Sum(state => state.DurationSeconds));
    }

    [Fact]
    public void Open_time_is_the_first_visible_point_while_earlier_windows_remain_in_state_history()
    {
        var model = CreateModel();
        model.SelectedTabIndex = 7;
        model.SetVisibleStart(30);

        model.Apply(Window(25, 8.5), 30);
        model.Apply(Window(31, 9.0), 30);

        var points = Assert.Single(Assert.Single(model.ActivePlots).Chart.Series).Points;
        Assert.Equal(new[] { 31d }, points.Select(point => point.X));
    }

    [Fact]
    public void Aperiodic_exponent_value_is_shown_without_treating_exponent_as_a_physical_unit()
    {
        var model = CreateModel();
        model.SetVisibleStart(30);
        model.Apply(Window(30, 60), 30);
        model.SetCursor(30);

        Assert.Equal("0.36", model.CorticalValue);
        Assert.Equal("60 Hz", model.IapfValue);
    }

    private static ReviewTnkAnalysisViewModel CreateModel()
    {
        var model = new ReviewTnkAnalysisViewModel();
        model.Initialize(new AlgorithmCatalogItem("official", "tnk_biofeedback", "tnk-v1", "TNK", "TNK", "", [],
            ["dynamic"], Schema(), new DynamicAnalysisPolicy(4, [4], 4, 1, true), "available", true,
            "definition", "1", "implementation"));
        return model;
    }

    private static JsonElement Schema() => JsonSerializer.SerializeToElement(new
    {
        children = new[]
        {
            new { algorithm_id = "tnk_composite_meditation", display_name_zh = "冥想趋势", required_roles = Array.Empty<string>() },
            new { algorithm_id = "tnk_iapf", display_name_zh = "个体 Alpha 峰", required_roles = new[] { "Pz", "Oz" } },
            new { algorithm_id = "tnk_aperiodic", display_name_zh = "非周期指数", required_roles = new[] { "Fz", "Pz", "Oz" } },
        },
    });

    private static DynamicWindowResponse Window(double end, double value)
    {
        var evidence = JsonSerializer.SerializeToElement(new
        {
            extensions = new { tnk_evidence = new { children = new
            {
                tnk_iapf = new[] { new { channel = "Pz/Oz", unit = "Hz", value,
                    actual_range = new { start_s = end - 4, end_s = end } } },
                tnk_aperiodic = new[] { new { channel = "Fz", unit = "exponent", value = 0.36,
                    actual_range = new { start_s = end - 30, end_s = end } } },
                tnk_composite_meditation = new[] { new { channel = "group", unit = "score", value,
                    actual_range = new { start_s = end - 4, end_s = end } } },
            } } },
        });
        return new DynamicWindowResponse("session", $"window-{end}", "request", "ready",
            JsonSerializer.SerializeToElement(new { algorithm_id = "tnk_biofeedback" }), null,
            new((long)((end - 4) * 1000), (long)(end * 1000), end - 4, end), "clean", null,
            new(null, "ready", "algorithm_group", null, null, null, null, "clean", null, evidence, null));
    }
}
