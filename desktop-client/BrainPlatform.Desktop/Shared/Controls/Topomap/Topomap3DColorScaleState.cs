namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

public sealed class Topomap3DColorScaleState
{
    private readonly Dictionary<(string Band, string Unit), (double Minimum, double Maximum)> absolute = [];

    public (double Minimum, double Maximum)? GetBounds(string band, string unit) => unit == "ratio"
        ? (0, 1)
        : ReadAbsolute(band, unit);

    public void Remember(string band, string unit, double? minimum, double? maximum)
    {
        if (unit != "uV^2" || minimum is not double low || maximum is not double high ||
            !double.IsFinite(low) || !double.IsFinite(high)) return;
        lock (absolute)
            absolute.TryAdd((band, unit), (low, high));
    }

    public void Reset()
    {
        lock (absolute) absolute.Clear();
    }

    private (double Minimum, double Maximum)? ReadAbsolute(string band, string unit)
    {
        lock (absolute)
            return absolute.TryGetValue((band, unit), out var bounds) ? bounds : null;
    }
}
