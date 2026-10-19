using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class FullRunWindowIntegrityTests
{
    [Fact]
    public void Backend_series_without_explicit_ids_keeps_positional_window_identity()
    {
        var preview = OnDemandDetailClient.FullPsdStructuredPreview() with
        {
            Windows = [JsonSerializer.SerializeToElement(new
            {
                start_sample = 0, end_sample = 1000, start_s = 0d, end_s = 5d,
                state = "Complete", quality = "clean", provenance = new { analysis_state = "Complete" },
            })],
        };
        FullRunPreviewIntegrity.RequireStructured(preview, Run());
        var response = FullRunWindowPreviewAdapter.ToDynamicWindowResponse(preview, Target(1000), 200);
        Assert.Equal("full-cache:full-run-1:window-0", response.WindowId);
        Assert.Equal("ready", response.State);
        Assert.Equal(9, response.Result!.Arrays!["psd"][0].GetDouble());
    }

    [Fact]
    public void Structured_usable_partial_preserves_backend_warmup_and_values()
    {
        var preview = OnDemandDetailClient.FullPsdStructuredPreview() with
        {
            Windows = [Window("first", 0, 800, "Partial", "clean")],
        };
        FullRunPreviewIntegrity.RequireStructured(preview, Run());
        var response = FullRunWindowPreviewAdapter.ToDynamicWindowResponse(preview, Target(800), 200);
        Assert.Equal("ready", response.State);
        Assert.Equal(800, response.Range!.EndSample);
        Assert.Equal("Partial", response.Result!.Provenance!.Value.GetProperty("analysis_state").GetString());
        Assert.Equal("retained", response.Result.Provenance.Value.GetProperty("evidence").GetString());
        Assert.Equal(9, response.Result.Arrays!["psd"][0].GetDouble());
    }

    [Fact]
    public void Indexed_usable_partial_preserves_backend_warmup()
    {
        var preview = OnDemandDetailClient.FullPsdWindowPreview(800);
        preview = preview with { Window = preview.Window! with { EndSample = 800, WindowState = "Partial" } };
        FullRunPreviewIntegrity.RequireIndexed(preview, Run(), Target(800));
        var response = FullRunWindowPreviewAdapter.ToDynamicWindowResponse(preview, Target(800), 200);
        Assert.Equal("ready", response.State);
        Assert.Equal("Partial", response.Result!.Provenance!.Value.GetProperty("analysis_state").GetString());
    }

    [Theory]
    [InlineData("Rejected", "rejected", "rejected")]
    [InlineData("Unavailable", "unavailable", "unavailable")]
    [InlineData("Complete", "failed", "unavailable")]
    public void Structured_unusable_window_is_not_replaced_by_older_valid_values(string state, string quality, string expected)
    {
        var preview = OnDemandDetailClient.FullPsdStructuredPreview() with
        {
            Windows = [Window("first", 0, 800, "Complete", "clean"), Window("second", 1, 1000, state, quality)],
            Arrays = new Dictionary<string, JsonElement>
            {
                ["psd"] = JsonSerializer.SerializeToElement(new double?[][] { [9, 8, 7], [null, null, null] }),
            },
        };
        FullRunPreviewIntegrity.RequireStructured(preview, Run());
        var response = FullRunWindowPreviewAdapter.ToDynamicWindowResponse(preview, Target(1000), 200);
        Assert.Equal(expected, response.State);
        Assert.Equal(1000, response.Range!.EndSample);
        Assert.Null(response.Result);
        Assert.Equal("QUALITY", response.Failure!.Code);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("unavailable")]
    public void Indexed_unusable_window_never_exposes_arrays(string state)
    {
        var preview = OnDemandDetailClient.FullPsdWindowPreview(1000) with { Status = state, ReasonCodes = ["QUALITY"] };
        FullRunPreviewIntegrity.RequireIndexed(preview, Run(), Target(1000));
        var response = FullRunWindowPreviewAdapter.ToDynamicWindowResponse(preview, Target(1000), 200);
        Assert.Equal(state, response.State);
        Assert.Null(response.Result);
        Assert.Equal("QUALITY", response.Failure!.Code);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("index")]
    [InlineData("matrix")]
    public void Structured_window_identity_and_time_frequency_shape_are_verified(string defect)
    {
        var preview = OnDemandDetailClient.FullStftStructuredPreview();
        var windows = preview.Windows.ToArray();
        if (defect != "matrix")
            windows[1] = Window(defect == "duplicate" ? "full-window-0" : "unique", 7, 2000, "Complete", "clean");
        else
            preview = preview with
            {
                Arrays = new Dictionary<string, JsonElement>
                {
                    ["power_linear"] = JsonSerializer.SerializeToElement(new[] { new[] { 1d }, new[] { 2d }, new[] { 3d } }),
                },
            };
        preview = preview with { Windows = windows };
        Assert.Throws<InvalidOperationException>(() => FullRunPreviewIntegrity.RequireStructured(preview, Run()));
    }

    private static JsonElement Window(string id, int index, long end, string state, string quality) =>
        JsonSerializer.SerializeToElement(new
        {
            window_id = id, window_index = index, start_sample = 0, end_sample = end, state, quality,
            provenance = new { evidence = "retained" },
            failure = new { code = "QUALITY", message = "Backend quality reason" },
        });

    private static AnalysisRunResponse Run() => new("full-run-1", "recording", "official_algorithm",
        "completed", "fixture-v1", null, null, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static DynamicWindowRequest Target(long sample) => new("recording", "psd", null, null,
        JsonSerializer.SerializeToElement(new { }), sample);
}
