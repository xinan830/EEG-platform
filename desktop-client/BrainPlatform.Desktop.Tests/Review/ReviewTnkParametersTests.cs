using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewTnkParametersTests
{
    [Fact]
    public void Configuration_keeps_all_five_distinct_roles_and_catalog_window_policy()
    {
        var parameters = Parameters();
        using var config = JsonDocument.Parse(parameters.BuildConfig(100, 500).GetRawText());
        var root = config.RootElement;

        Assert.Equal("tnk_biofeedback", root.GetProperty("algorithm_id").GetString());
        Assert.Equal("tnk-fixture-v1", root.GetProperty("scientific_version").GetString());
        Assert.Equal("dynamic", root.GetProperty("mode").GetString());
        Assert.Equal(4, root.GetProperty("dynamic_window_s").GetDouble());
        Assert.Equal(1, root.GetProperty("refresh_step_s").GetDouble());
        Assert.Equal(new[] { "Fz-REF", "Pz-REF", "Oz-REF", "F3-REF", "F4-REF" },
            new[] { "Fz", "Pz", "Oz", "F3", "F4" }.Select(role =>
                root.GetProperty("tnk_role_mapping").GetProperty(role).GetString()).ToArray());
    }

    [Fact]
    public void Missing_or_duplicate_role_sources_are_rejected()
    {
        var parameters = Parameters();
        parameters.Roles.Single(role => role.Role == "Oz").SelectedChannel = null;
        Assert.Throws<InvalidOperationException>(() => parameters.BuildConfig(100, 500));

        parameters.Roles.Single(role => role.Role == "Oz").SelectedChannel = "Pz-REF";
        Assert.Throws<InvalidOperationException>(() => parameters.BuildConfig(100, 500));
    }

    private static ReviewTnkParameters Parameters()
    {
        var parameters = new ReviewTnkParameters();
        var recording = new RegisteredRecording("recording", "fixture", ".wpf", "now", 500, 100,
            ["Fz-REF", "Pz-REF", "Oz-REF", "F3-REF", "F4-REF"], "sha");
        var outputs = new[] { "Fz", "Pz", "Oz", "F3", "F4" }
            .Select(role => new HistoricalMontageOutput($"{role}-REF", role, role, "V", true, null)).ToArray();
        var montage = new HistoricalMontageDescriptor("montage", "1", "fingerprint", "fixture", true, true, null, outputs);
        parameters.Initialize(recording, new AlgorithmCatalogItem("official", "tnk_biofeedback", "tnk-fixture-v1",
            "TNK", "TNK", "", [], ["dynamic"], default,
            new DynamicAnalysisPolicy(4, [4], 4, 1, true), "available", true,
            "tnk-definition", "1", "tnk-implementation"), montage);
        return parameters;
    }
}
