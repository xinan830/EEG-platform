using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewScalarParametersTests
{
    [Fact]
    public void Faa_maps_only_ordered_f3_f4_roles_from_the_selected_montage()
    {
        var parameters = new ReviewScalarParameters("faa", "F3", "F4", "f4_channel");
        var recording = Recording();
        var montage = Montage(
            new("F3-REF", "F3", "F3", "V", true, null),
            new("F4-REF", "F4", "F4", "V", true, null),
            new("Pz-REF", "Pz", "Pz", "V", true, null));
        parameters.Initialize(recording, Algorithm("faa"), montage);

        using var config = JsonDocument.Parse(parameters.BuildConfig(100, 500).GetRawText());
        Assert.Equal("F3-REF", config.RootElement.GetProperty("channel").GetString());
        Assert.Equal("F4-REF", config.RootElement.GetProperty("f4_channel").GetString());
        Assert.Equal(new[] { "F3-REF", "F4-REF" }, config.RootElement.GetProperty("channels")
            .EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(new[] { "F3", "F4" }, config.RootElement.GetProperty("analysis_montage_outputs")
            .EnumerateArray().Select(item => item.GetProperty("role").GetString()).ToArray());
    }

    [Fact]
    public void Paired_metric_without_required_role_fails_instead_of_substituting_a_channel()
    {
        var parameters = new ReviewScalarParameters("brainbeat", "Fz", "Pz", "secondary_channel");
        parameters.Initialize(Recording(), Algorithm("brainbeat"), Montage(
            new("Fz", "Fz", "Fz", "V", true, null),
            new("Cz", "Cz", "Cz", "V", true, null)));

        Assert.Throws<InvalidOperationException>(() => parameters.BuildConfig(100, 500));
    }

    [Fact]
    public void Brainbeat_preserves_fz_pz_order_and_single_channel_metrics_do_not_add_pair_inputs()
    {
        var montage = Montage(
            new("Fz-REF", "Fz", "Fz", "V", true, null),
            new("Pz-REF", "Pz", "Pz", "V", true, null));
        var brainbeat = new ReviewScalarParameters("brainbeat", "Fz", "Pz", "secondary_channel");
        brainbeat.Initialize(Recording(), Algorithm("brainbeat"), montage);
        using var pair = JsonDocument.Parse(brainbeat.BuildConfig(100, 500).GetRawText());
        Assert.Equal("Fz-REF", pair.RootElement.GetProperty("channel").GetString());
        Assert.Equal("Pz-REF", pair.RootElement.GetProperty("secondary_channel").GetString());
        Assert.Equal(new[] { "Fz-REF", "Pz-REF" }, pair.RootElement.GetProperty("channels")
            .EnumerateArray().Select(item => item.GetString()).ToArray());

        var iapf = new ReviewScalarParameters("iapf");
        iapf.Initialize(Recording(), Algorithm("iapf"), null);
        using var single = JsonDocument.Parse(iapf.BuildConfig(100, 500).GetRawText());
        Assert.False(single.RootElement.TryGetProperty("secondary_channel", out _));
        Assert.Single(single.RootElement.GetProperty("channels").EnumerateArray());
        Assert.Equal(20, single.RootElement.GetProperty("dynamic_window_s").GetDouble());
        Assert.Equal(1, single.RootElement.GetProperty("refresh_step_s").GetDouble());

        var individualized = new ReviewScalarParameters("brainbeat_individualized", "Fz", "Pz", "secondary_channel");
        individualized.Initialize(Recording(), Algorithm("brainbeat_individualized"), montage);
        using var individualConfig = JsonDocument.Parse(individualized.BuildConfig(100, 500).GetRawText());
        Assert.Equal("brainbeat_individualized", individualConfig.RootElement.GetProperty("algorithm_id").GetString());
        Assert.Equal(20, individualConfig.RootElement.GetProperty("dynamic_window_s").GetDouble());
        Assert.Equal(1, individualConfig.RootElement.GetProperty("refresh_step_s").GetDouble());
    }

    [Fact]
    public void ThetaBeta_review_serializes_the_selected_frequency_mode()
    {
        var parameters = new ReviewScalarParameters("theta_beta");
        parameters.Initialize(Recording(), Algorithm("theta_beta"), null);
        using var defaults = JsonDocument.Parse(parameters.BuildConfig(100, 500).GetRawText());
        Assert.Equal("standard", defaults.RootElement.GetProperty("frequency_mode").GetString());
        parameters.FrequencyMode = "individualized";

        using var config = JsonDocument.Parse(parameters.BuildConfig(100, 500).GetRawText());
        Assert.Equal("individualized", config.RootElement.GetProperty("frequency_mode").GetString());
        Assert.Equal(20, config.RootElement.GetProperty("dynamic_window_s").GetDouble());
        Assert.Equal(1, config.RootElement.GetProperty("refresh_step_s").GetDouble());
    }

    private static RegisteredRecording Recording() => new(
        "registered", "fixture", ".wpf", "now", 500, 100, ["F3", "F4", "Fz", "Pz", "Cz"], "sha");

    private static AlgorithmCatalogItem Algorithm(string id) => new(
        "official", id, "fixture-v1", id, id, "", [], ["dynamic"], default,
        id is "iapf" or "theta_beta" ? new DynamicAnalysisPolicy(20, [20, 30], 20, 1, false)
            : id is "brainbeat" or "brainbeat_individualized" ? new DynamicAnalysisPolicy(10, [10, 20, 30], 20, 1, false)
            : new DynamicAnalysisPolicy(4, [5], 5, 1, false), "available", true,
        "definition", "1", "impl");

    private static HistoricalMontageDescriptor Montage(params HistoricalMontageOutput[] outputs) => new(
        "montage", "1", "fingerprint", "fixture", true, true, null, outputs);
}
