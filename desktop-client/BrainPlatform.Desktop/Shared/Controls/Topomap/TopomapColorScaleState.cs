using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

public sealed class TopomapColorScaleState
{
    private readonly Dictionary<(string BandId, string Unit), (double Minimum, double Maximum)> absoluteBounds = [];

    public (double Minimum, double Maximum)? GetBounds(TopomapBand band)
    {
        if (band.Unit == "ratio") return (0, 1);
        if (band.Unit != "uV^2") return null;

        lock (absoluteBounds)
            return absoluteBounds.TryGetValue((band.BandId, band.Unit), out var bounds) ? bounds : null;
    }

    public void Remember(TopomapBand band, double minimum, double maximum)
    {
        if (band.Unit != "uV^2" || !double.IsFinite(minimum) || !double.IsFinite(maximum)) return;
        lock (absoluteBounds)
            absoluteBounds.TryAdd((band.BandId, band.Unit), (minimum, maximum));
    }

    public void Reset()
    {
        lock (absoluteBounds) absoluteBounds.Clear();
    }
}
