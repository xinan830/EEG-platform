using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

public sealed class TopomapColorScaleState
{
    private readonly Dictionary<(string FieldId, string Unit), (double Minimum, double Maximum)> absoluteBounds = [];

    public (double Minimum, double Maximum)? GetBounds(TopomapField band)
    {
        if (band.Unit == "ratio") return (0, 1);
        if (band.Unit is not ("uV^2" or "V^2/Hz")) return null;

        lock (absoluteBounds)
            return absoluteBounds.TryGetValue((band.FieldId, band.Unit), out var bounds) ? bounds : null;
    }

    public void Remember(TopomapField band, double minimum, double maximum)
    {
        if (band.Unit is not ("uV^2" or "V^2/Hz") || !double.IsFinite(minimum) || !double.IsFinite(maximum)) return;
        lock (absoluteBounds)
            absoluteBounds.TryAdd((band.FieldId, band.Unit), (minimum, maximum));
    }

    public void Reset()
    {
        lock (absoluteBounds) absoluteBounds.Clear();
    }
}
