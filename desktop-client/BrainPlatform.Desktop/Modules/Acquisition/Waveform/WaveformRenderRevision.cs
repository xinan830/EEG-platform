
namespace BrainPlatform.Desktop.Views;

internal sealed record WaveformRenderRevision(
    int BatchCount,
    long FirstSampleCounter,
    long LastSampleCounter,
    double DisplayWindowSeconds,
    int HorizontalPixels,
    double SensitivityMicrovoltsPerMillimeter,
    double PlotHeight,
    string ChannelSignature,
    string AdjustmentSignature)
{
    public static WaveformRenderRevision Create(
        LiveWaveformSource source,
        double displayWindowSeconds,
        int horizontalPixels,
        double sensitivityMicrovoltsPerMillimeter,
        double plotHeight)
    {
        var firstCounter = source.Batches.Count == 0 ? -1 : source.Batches[0].FirstSampleCounter;
        var lastCounter = source.Batches.Count == 0 ? -1 : source.Batches[^1].LastSampleCounter;
        var channels = string.Join(';', source.Channels.Select(channel => $"{channel.StreamIndex}:{channel.Label}"));
        var adjustments = string.Join(';', (source.DisplayCounterAdjustments ?? [])
            .Select(value => $"{value.EffectiveFromRawSampleCounter}:{value.SkippedSampleCountBefore}"));
        return new WaveformRenderRevision(
            source.Batches.Count,
            firstCounter,
            lastCounter,
            displayWindowSeconds,
            horizontalPixels,
            sensitivityMicrovoltsPerMillimeter,
            plotHeight,
            channels,
            adjustments);
    }
}
