namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.BandRatio;

public sealed partial record BandRatioResultPreview
{
    public static BandRatioResultPreview FromWindow(DynamicWindowResponse response, string channel,
        BandRatioResultPreview? previous = null)
    {
        var projection = ScalarWindowProjectionParser.Parse(response, "ratio", "ratio", "band_ratio_evidence");
        var specific = projection.Evidence;
        var evidence = new BandRatioEvidence(
            ScalarWindowProjectionParser.Range(specific, "numerator_band_hz", 0),
            ScalarWindowProjectionParser.Range(specific, "numerator_band_hz", 1),
            ScalarWindowProjectionParser.Range(specific, "denominator_band_hz", 0),
            ScalarWindowProjectionParser.Range(specific, "denominator_band_hz", 1),
            ScalarWindowProjectionParser.Number(specific, "numerator_power_v2"),
            ScalarWindowProjectionParser.Number(specific, "denominator_power_v2"),
            ScalarWindowProjectionParser.Text(projection.CalculationTrace, "formula"));
        var point = new BandRatioResultPoint(projection.TimeSeconds, projection.StartSeconds,
            projection.EndSeconds, projection.Value, projection.State, projection.Quality, projection.Failure,
            evidence);
        return new BandRatioResultPreview(channel, null,
            ScalarWindowProjectionParser.Merge(previous?.DynamicPoints ?? [], point,
                item => item.StartSeconds, item => item.EndSeconds));
    }
}
