
namespace BrainPlatform.Desktop.Modules.Review.Waveform;

internal sealed record ResolvedMontageChannel(
    DerivedMontageChannel Definition,
    int PositiveIndex,
    int[] NegativeIndexes,
    string? ReferenceKey);

/// <summary>
/// Precomputes shared mean reference signals for one display-frame build.
/// The cache owns derived arrays only and never mutates raw acquisition batches.
/// </summary>
internal sealed class ReferenceSignalCache
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<AcquisitionBatch, double[]>> valuesByReference;

    private ReferenceSignalCache(
        IReadOnlyDictionary<string, IReadOnlyDictionary<AcquisitionBatch, double[]>> valuesByReference,
        int computedReferenceSampleCount)
    {
        this.valuesByReference = valuesByReference;
        ComputedReferenceSampleCount = computedReferenceSampleCount;
    }

    internal int ComputedReferenceSampleCount { get; }

    internal static ReferenceSignalCache Create(
        IReadOnlyList<AcquisitionBatch> batches,
        IReadOnlyList<ResolvedMontageChannel> channels)
    {
        var references = channels
            .Where(channel => channel.ReferenceKey is not null)
            .GroupBy(channel => channel.ReferenceKey!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().NegativeIndexes,
                StringComparer.Ordinal);
        var valuesByReference = new Dictionary<string, IReadOnlyDictionary<AcquisitionBatch, double[]>>(
            references.Count,
            StringComparer.Ordinal);
        var computedReferenceSampleCount = 0;

        foreach (var (referenceKey, streamIndexes) in references)
        {
            var valuesByBatch = new Dictionary<AcquisitionBatch, double[]>(batches.Count);
            foreach (var batch in batches)
            {
                var values = new double[batch.SampleCount];
                for (var sampleIndex = 0; sampleIndex < batch.SampleCount; sampleIndex++)
                {
                    values[sampleIndex] = CalculateMean(batch, sampleIndex, streamIndexes);
                }

                computedReferenceSampleCount += batch.SampleCount;
                valuesByBatch.Add(batch, values);
            }

            valuesByReference.Add(referenceKey, valuesByBatch);
        }

        return new ReferenceSignalCache(valuesByReference, computedReferenceSampleCount);
    }

    internal double Read(string referenceKey, AcquisitionBatch batch, int sampleIndex)
    {
        if (sampleIndex < 0 || sampleIndex >= batch.SampleCount ||
            GetValues(referenceKey, batch) is not { } values)
        {
            return double.NaN;
        }

        return values[sampleIndex];
    }

    internal double[]? GetValues(string referenceKey, AcquisitionBatch batch) =>
        valuesByReference.TryGetValue(referenceKey, out var valuesByBatch) &&
        valuesByBatch.TryGetValue(batch, out var values)
            ? values
            : null;

    private static double CalculateMean(
        AcquisitionBatch batch,
        int sampleIndex,
        IReadOnlyList<int> streamIndexes)
    {
        if (streamIndexes.Count == 0)
        {
            return double.NaN;
        }

        var sampleOffset = checked(sampleIndex * batch.ChannelCount);
        var sum = 0d;
        foreach (var streamIndex in streamIndexes)
        {
            if (streamIndex < 0 || streamIndex >= batch.ChannelCount)
            {
                return double.NaN;
            }

            var value = batch.SampleMajorValues[sampleOffset + streamIndex];
            if (!double.IsFinite(value))
            {
                return double.NaN;
            }

            sum += value;
        }

        return sum / streamIndexes.Count;
    }
}
