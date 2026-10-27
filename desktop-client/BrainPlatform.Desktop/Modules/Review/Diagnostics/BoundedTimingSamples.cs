namespace BrainPlatform.Desktop.Modules.Review.Diagnostics;

internal readonly record struct TimingPercentiles(int Count, double P50, double P95, double P99);

/// <summary>Stores a fixed-capacity rolling sample set for low-overhead diagnostics.</summary>
internal sealed class BoundedTimingSamples(int capacity)
{
    private readonly double[] values = capacity > 0
        ? new double[capacity]
        : throw new ArgumentOutOfRangeException(nameof(capacity));
    private int nextIndex;
    private int count;

    internal void Add(double value)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        values[nextIndex] = value;
        nextIndex = (nextIndex + 1) % values.Length;
        count = Math.Min(count + 1, values.Length);
    }

    internal TimingPercentiles Summarize()
    {
        if (count == 0) return new TimingPercentiles(0, 0, 0, 0);

        var ordered = new double[count];
        var startIndex = (nextIndex - count + values.Length) % values.Length;
        for (var index = 0; index < count; index++)
            ordered[index] = values[(startIndex + index) % values.Length];
        Array.Sort(ordered);

        return new TimingPercentiles(count,
            Percentile(ordered, 0.50),
            Percentile(ordered, 0.95),
            Percentile(ordered, 0.99));
    }

    internal void Clear()
    {
        Array.Clear(values);
        count = 0;
        nextIndex = 0;
    }

    private static double Percentile(double[] ordered, double percentile)
    {
        var index = Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }
}
