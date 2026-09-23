using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Montages;

public sealed record ProjectedMontageChannel(string Label, double[] Values);

public sealed record ProjectedMontageSegment(
    long FirstSampleCounter,
    int SampleCount,
    IReadOnlyList<ProjectedMontageChannel> Channels);

/// <summary>
/// Creates read-only V/float64 display traces from a raw review window.
/// This is display montage math only; filters and scientific analysis remain
/// backend-owned, and source arrays are never changed.
/// </summary>
public static class MontageDisplayProjector
{
    public static IReadOnlyList<ProjectedMontageSegment> Project(
        RecordingReviewWindow window,
        LocalRawRecordingManifest manifest,
        MontageProfile? montage)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(manifest);
        var sourceIndexes = BuildSourceIndexes(manifest);
        var definitions = montage?.DerivedChannels
            .OrderBy(channel => channel.DisplayOrder)
            .ToArray()
            ?? manifest.Channels
                .Where(IsSignalChannel)
                .Where(channel => !string.IsNullOrWhiteSpace(channel.Label))
                .OrderBy(channel => channel.StreamIndex)
                .Select(channel => new DerivedMontageChannel(
                    channel.Label!,
                    channel.Label!,
                    MontageNegativeKind.OriginalHardwareReference,
                    [],
                    channel.StreamIndex))
                .ToArray();

        var resolved = definitions.Select(definition => Resolve(definition, sourceIndexes)).ToArray();
        return window.Segments
            .Select(segment => ProjectSegment(segment, resolved))
            .ToArray();
    }

    private static ProjectedMontageSegment ProjectSegment(
        RecordingReviewSegment segment,
        IReadOnlyList<ResolvedDefinition> definitions)
    {
        var meanReferences = definitions
            .Where(definition => definition.Definition.NegativeKind is MontageNegativeKind.Mean or MontageNegativeKind.SpecifiedPair)
            .GroupBy(definition => string.Join(',', definition.NegativeIndexes.OrderBy(index => index)), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().NegativeIndexes, StringComparer.Ordinal);
        var referenceValues = meanReferences.ToDictionary(
            pair => pair.Key,
            pair => BuildReferenceValues(segment, pair.Value),
            StringComparer.Ordinal);
        var channels = definitions
            .Select(definition => new ProjectedMontageChannel(
                definition.Definition.Name,
                BuildValues(segment, definition, referenceValues)))
            .ToArray();
        return new ProjectedMontageSegment(segment.FirstSampleCounter, segment.SampleCount, channels);
    }

    private static double[] BuildValues(
        RecordingReviewSegment segment,
        ResolvedDefinition definition,
        IReadOnlyDictionary<string, double[]> referenceValues)
    {
        var values = new double[segment.SampleCount];
        var referenceKey = string.Join(',', definition.NegativeIndexes.OrderBy(index => index));
        for (var sampleIndex = 0; sampleIndex < values.Length; sampleIndex++)
        {
            var positive = ReadValue(segment, sampleIndex, definition.PositiveIndex);
            if (!double.IsFinite(positive))
            {
                values[sampleIndex] = double.NaN;
                continue;
            }

            if (definition.Definition.NegativeKind == MontageNegativeKind.OriginalHardwareReference)
            {
                values[sampleIndex] = positive;
                continue;
            }

            var reference = definition.Definition.NegativeKind is MontageNegativeKind.Mean or MontageNegativeKind.SpecifiedPair
                ? referenceValues[referenceKey][sampleIndex]
                : CalculateDirectReference(segment, sampleIndex, definition.NegativeIndexes);
            values[sampleIndex] = double.IsFinite(reference) ? positive - reference : double.NaN;
        }

        return values;
    }

    private static double[] BuildReferenceValues(RecordingReviewSegment segment, IReadOnlyList<int> indexes)
    {
        var values = new double[segment.SampleCount];
        for (var sampleIndex = 0; sampleIndex < values.Length; sampleIndex++)
        {
            values[sampleIndex] = CalculateDirectReference(segment, sampleIndex, indexes);
        }

        return values;
    }

    private static double CalculateDirectReference(
        RecordingReviewSegment segment,
        int sampleIndex,
        IReadOnlyList<int> indexes)
    {
        if (indexes.Count == 0)
        {
            return double.NaN;
        }

        var sum = 0d;
        foreach (var index in indexes)
        {
            var value = ReadValue(segment, sampleIndex, index);
            if (!double.IsFinite(value))
            {
                return double.NaN;
            }

            sum += value;
        }

        return sum / indexes.Count;
    }

    private static ResolvedDefinition Resolve(
        DerivedMontageChannel definition,
        IReadOnlyDictionary<string, int> sourceIndexes)
    {
        if (!sourceIndexes.TryGetValue(definition.PositiveLabel.Trim(), out var positiveIndex))
        {
            throw new InvalidOperationException($"回溯导联缺少正极通道“{definition.PositiveLabel}”。");
        }

        var negativeIndexes = definition.NegativeLabels
            .Select(label => sourceIndexes.TryGetValue(label.Trim(), out var index)
                ? index
                : throw new InvalidOperationException($"回溯导联缺少参考通道“{label}”。"))
            .ToArray();
        return new ResolvedDefinition(definition, positiveIndex, negativeIndexes);
    }

    private static IReadOnlyDictionary<string, int> BuildSourceIndexes(LocalRawRecordingManifest manifest)
    {
        var groups = manifest.Channels
            .Where(IsSignalChannel)
            .Where(channel => !string.IsNullOrWhiteSpace(channel.Label))
            .GroupBy(channel => channel.Label!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var duplicate = groups.FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"回溯记录的通道标签“{duplicate.Key}”无法唯一映射。");
        }

        return groups.ToDictionary(group => group.Key, group => group.Single().StreamIndex, StringComparer.OrdinalIgnoreCase);
    }

    private static double ReadValue(RecordingReviewSegment segment, int sampleIndex, int streamIndex) =>
        streamIndex >= 0 && streamIndex < segment.ChannelCount
            ? segment.SampleMajorValues[checked(sampleIndex * segment.ChannelCount + streamIndex)]
            : double.NaN;

    private static bool IsSignalChannel(AcquisitionChannel channel) =>
        channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar;

    private sealed record ResolvedDefinition(
        DerivedMontageChannel Definition,
        int PositiveIndex,
        IReadOnlyList<int> NegativeIndexes);
}
