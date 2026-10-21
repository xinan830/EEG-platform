using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class FullRunProvenanceTests
{
    [Theory]
    [InlineData("provenance")]
    [InlineData("preprocessing_scope")]
    [InlineData("inner_time_axis")]
    [InlineData("invalid_scope")]
    [InlineData("invalid_axis")]
    public void Indexed_stft_rejects_missing_or_invalid_backend_contract(string defect)
    {
        var preview = Indexed();
        var provenance = preview.Window!.Provenance!.Value.EnumerateObject()
            .Where(property => property.Name != defect)
            .ToDictionary(property => property.Name, property => property.Value);
        if (defect == "invalid_scope") provenance["preprocessing_scope"] = JsonSerializer.SerializeToElement("unknown");
        if (defect == "invalid_axis") provenance["inner_time_axis"] = JsonSerializer.SerializeToElement("unknown");
        preview = preview with { Window = preview.Window with
        {
            Provenance = defect == "provenance" ? null : JsonSerializer.SerializeToElement(provenance),
        }};

        var response = FullRunWindowPreviewAdapter.ToDynamicWindowResponse(preview, Target(), 200);
        var state = new StftPreviewState();
        StftWindowPresenter.Apply(state, response);

        Assert.Equal("unavailable", response.State);
        Assert.Null(response.Result);
        Assert.Null(state.Result);
        Assert.Equal("FULL_WINDOW_CONTRACT_UNAVAILABLE", response.Failure!.Code);
        Assert.Contains("重新计算", response.Failure.Message);
        Assert.Equal(1000, response.Range!.StartSample);
        Assert.Equal(2000, response.Range.EndSample);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Both_preview_paths_preserve_real_backend_evidence_and_values(bool indexed)
    {
        var response = indexed
            ? FullRunWindowPreviewAdapter.ToDynamicWindowResponse(Indexed(), Target(), 200)
            : FullRunWindowPreviewAdapter.ToDynamicWindowResponse(
                OnDemandDetailClient.FullStftStructuredPreview(), Target(), 200);
        var state = new StftPreviewState();
        StftWindowPresenter.Apply(state, response);

        Assert.Equal("ready", response.State);
        Assert.NotNull(state.Result);
        Assert.Equal([7d, 8d], state.Result!.TimesSeconds);
        Assert.Equal("dB re 1 uV^2/Hz", state.Result.PowerUnit);
        Assert.Equal("whole_recording_before_window_slice",
            response.Result!.Provenance!.Value.GetProperty("preprocessing_scope").GetString());
        Assert.False(response.Result.Provenance.Value.TryGetProperty("compatibility_source", out _));
        Assert.Equal(2d, response.Result.Arrays!["power_db"][0][0].GetDouble());
    }

    private static WindowPreviewResponse Indexed()
    {
        var structured = OnDemandDetailClient.FullStftStructuredPreview();
        return OnDemandDetailClient.FullPsdWindowPreview(2000) with
        {
            Output = structured.Output,
            Window = new WindowPreviewWindow("full-window-1", 1, 1000, 2000, "Complete", "valid", [],
                structured.Windows[1].GetProperty("provenance")),
            Axes = structured.Axes,
            AxisMetadata = structured.AxisMetadata,
            Arrays = structured.Arrays.ToDictionary(pair => pair.Key, pair => pair.Value[1]),
            ArrayMetadata = structured.ArrayMetadata,
        };
    }

    private static DynamicWindowRequest Target() => new("recording", "stft", null, null,
        JsonSerializer.SerializeToElement(new { }), 2000);
}
