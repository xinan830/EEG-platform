using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

internal sealed record ProjectedPairedTraceRow(
    string ChannelName,
    double[]? BaselineX,
    double[]? BaselineY,
    double[]? ProcessedX,
    double[]? ProcessedY,
    bool DrawBaseline,
    bool DrawProcessed,
    bool IsInterpolated);

/// <summary>Pure projection mapping paired scientific preview envelopes onto Review row coordinates.</summary>
internal static class ReviewPreprocessingOverlayProjection
{
    public static ProjectedPairedTraceRow ProjectRow(
        PreprocessingPreviewWindowResponse preview,
        string channelName,
        int rowIndex,
        int totalRows,
        double displayScale)
    {
        var baselineOffset = totalRows - rowIndex - 0.5;
        IReadOnlyList<double?>? baseValues = null;
        IReadOnlyList<double?>? procValues = null;
        var hasBaseline = TryGetTrace(preview.Baseline.TraceTimesSeconds, channelName, out var baseTimes) &&
                          TryGetTrace(preview.Baseline.TracesUv, channelName, out baseValues) &&
                          baseTimes is { Count: > 0 } && baseValues is { Count: > 0 };
        var hasProcessed = TryGetTrace(preview.Processed.TraceTimesSeconds, channelName, out var procTimes) &&
                           TryGetTrace(preview.Processed.TracesUv, channelName, out procValues) &&
                           procTimes is { Count: > 0 } && procValues is { Count: > 0 };
        var isInterpolated = preview.Processed.InterpolatedChannels.Contains(channelName, StringComparer.OrdinalIgnoreCase);

        // When all enhanced policies are off, baseline and processed are identical;
        // draw only processed dark gray once to avoid visual thickening from double rendering.
        var drawBaseline = hasBaseline && (preview.EnhancedPreprocessingActive || !hasProcessed);
        var drawProcessed = hasProcessed;

        double[]? bx = null, by = null, px = null, py = null;
        if (drawBaseline && baseTimes is not null && baseValues is not null)
            (bx, by) = BuildCoords(baseTimes, baseValues, baselineOffset, displayScale);
        if (drawProcessed && procTimes is not null && procValues is not null)
            (px, py) = BuildCoords(procTimes, procValues, baselineOffset, displayScale);

        return new ProjectedPairedTraceRow(channelName, bx, by, px, py, drawBaseline, drawProcessed, isInterpolated);
    }

    private static (double[] X, double[] Y) BuildCoords(
        IReadOnlyList<double> times,
        IReadOnlyList<double?> valuesUv,
        double baselineOffset,
        double displayScale)
    {
        var count = times.Count;
        var xs = new double[count];
        var ys = new double[count];
        for (var k = 0; k < count; k++)
        {
            xs[k] = times[k];
            ys[k] = valuesUv[k] is { } uv ? baselineOffset + uv * displayScale : double.NaN;
        }
        return (xs, ys);
    }

    internal static bool TryGetTrace<T>(
        IReadOnlyDictionary<string, IReadOnlyList<T>> traces,
        string channelName,
        out IReadOnlyList<T>? trace)
    {
        if (traces.TryGetValue(channelName, out trace)) return true;
        foreach (var pair in traces)
        {
            if (string.Equals(pair.Key, channelName, StringComparison.OrdinalIgnoreCase))
            {
                trace = pair.Value;
                return true;
            }
        }
        trace = null;
        return false;
    }
}
