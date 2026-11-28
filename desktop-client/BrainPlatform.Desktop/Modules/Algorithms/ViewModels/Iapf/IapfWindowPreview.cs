namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Iapf;

public sealed partial record IapfResultPreview
{
    public static IapfResultPreview FromWindow(DynamicWindowResponse response, string channel,
        IapfResultPreview? previous = null)
    {
        var projection = ScalarWindowProjectionParser.Parse(response, "iapf_hz", "Hz", "iapf_evidence");
        var splitPeak = projection.Evidence.ValueKind == System.Text.Json.JsonValueKind.Object &&
            projection.Evidence.TryGetProperty("split_peak", out var splitEl) &&
            splitEl.ValueKind == System.Text.Json.JsonValueKind.True;
        var point = new IapfResultPoint(projection.TimeSeconds, projection.StartSeconds,
            projection.EndSeconds, projection.Value, projection.State, projection.Quality, projection.Failure,
            ScalarWindowProjectionParser.Text(projection.Evidence, "source"),
            ScalarWindowProjectionParser.Number(projection.Evidence, "peak_hz"),
            ScalarWindowProjectionParser.Number(projection.Evidence, "cog_hz"),
            ScalarWindowProjectionParser.Number(projection.Evidence, "model_r2"),
            splitPeak);
        return new IapfResultPreview(channel, null,
            ScalarWindowProjectionParser.Merge(previous?.DynamicPoints ?? [], point,
                item => item.StartSeconds, item => item.EndSeconds));
    }
}
