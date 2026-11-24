namespace BrainPlatform.Desktop.Shared.Waveform;

internal static class SignalPreviewExtremaProjection
{
    public static (double[] TimeSeconds, double[] Microvolts) ToVerticalSegments(
        IReadOnlyList<double> binCentersSeconds,
        SignalPreviewChannelExtrema extrema)
    {
        if (binCentersSeconds.Count != extrema.MinimumMicrovolts.Count ||
            binCentersSeconds.Count != extrema.MaximumMicrovolts.Count)
            throw new ArgumentException("波形区间极值与时间轴长度不一致。", nameof(extrema));

        var timeSeconds = new double[binCentersSeconds.Count * 3];
        var microvolts = new double[timeSeconds.Length];
        for (var index = 0; index < binCentersSeconds.Count; index++)
        {
            var time = binCentersSeconds[index];
            var minimum = extrema.MinimumMicrovolts[index];
            var maximum = extrema.MaximumMicrovolts[index];
            if (!double.IsFinite(time) || !double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum)
                throw new ArgumentException("波形区间极值包含无效数据。", nameof(extrema));

            var offset = index * 3;
            timeSeconds[offset] = time;
            timeSeconds[offset + 1] = time;
            timeSeconds[offset + 2] = time;
            microvolts[offset] = minimum;
            microvolts[offset + 1] = maximum;
            microvolts[offset + 2] = double.NaN;
        }

        return (timeSeconds, microvolts);
    }
}
