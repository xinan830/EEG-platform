namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.PeakFrequency;

public sealed partial record PeakFrequencyResultPreview
{
    public static PeakFrequencyResultPreview FromWindow(DynamicWindowResponse response, string channel,
        PeakFrequencyResultPreview? previous = null)
    {
        var projection = ScalarWindowProjectionParser.Parse(response, "peak_frequency_hz", "Hz", "peak_frequency_evidence");
        var point = new PeakFrequencyResultPoint(projection.TimeSeconds, projection.StartSeconds,
            projection.EndSeconds, projection.Value, projection.State, projection.Quality, projection.Failure,
            ScalarWindowProjectionParser.Number(projection.Evidence, "selected_power_v2_per_hz"));
        return new PeakFrequencyResultPreview(channel, null,
            ScalarWindowProjectionParser.Merge(previous?.DynamicPoints ?? [], point,
                item => item.StartSeconds, item => item.EndSeconds));
    }
}
