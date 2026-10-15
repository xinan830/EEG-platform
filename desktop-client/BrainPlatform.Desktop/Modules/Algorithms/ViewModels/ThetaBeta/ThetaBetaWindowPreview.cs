namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.ThetaBeta;

public sealed partial record ThetaBetaResultPreview
{
    public static ThetaBetaResultPreview FromWindow(DynamicWindowResponse response, string channel,
        ThetaBetaResultPreview? previous = null)
    {
        var projection = ScalarWindowProjectionParser.Parse(response, "theta_beta", "dimensionless", "theta_beta_evidence");
        var specific = projection.Evidence;
        var evidence = new ThetaBetaEvidence(
            ScalarWindowProjectionParser.Number(specific, "iapf_hz"),
            ScalarWindowProjectionParser.Range(specific, "theta_range_hz", 0),
            ScalarWindowProjectionParser.Range(specific, "theta_range_hz", 1),
            ScalarWindowProjectionParser.Range(specific, "beta_range_hz", 0),
            ScalarWindowProjectionParser.Range(specific, "beta_range_hz", 1),
            ScalarWindowProjectionParser.Number(specific, "theta_power_uv2"),
            ScalarWindowProjectionParser.Number(specific, "beta_power_uv2"),
            ScalarWindowProjectionParser.Text(projection.CalculationTrace, "formula"));
        var point = new ThetaBetaResultPoint(projection.TimeSeconds, projection.StartSeconds,
            projection.EndSeconds, projection.Value, projection.State, projection.Quality, projection.Failure,
            evidence);
        return new ThetaBetaResultPreview(channel, null,
            ScalarWindowProjectionParser.Merge(previous?.DynamicPoints ?? [], point,
                item => item.StartSeconds, item => item.EndSeconds));
    }
}
