using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

/// <summary>Backend-owned reconstruction gaps, separate from detected artifacts.</summary>
public sealed record ArtifactRemovalUnavailableInterval(
    [property: JsonPropertyName("start_sample")] long StartSample,
    [property: JsonPropertyName("end_sample")] long EndSample,
    [property: JsonPropertyName("start_s")] double StartSeconds,
    [property: JsonPropertyName("end_s")] double EndSeconds,
    [property: JsonPropertyName("missing_input_channels")] IReadOnlyList<string> MissingInputChannels,
    [property: JsonPropertyName("unavailable_output_channels")] IReadOnlyList<string> UnavailableOutputChannels,
    [property: JsonPropertyName("additional_unavailable_channels")] IReadOnlyList<string> AdditionalUnavailableChannels,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record ArtifactRemovalValidity(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("sampling_rate_hz")] double SamplingRateHz,
    [property: JsonPropertyName("start_sample")] long StartSample,
    [property: JsonPropertyName("end_sample")] long EndSample,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels,
    [property: JsonPropertyName("unavailable_sample_count")] long UnavailableSampleCount,
    [property: JsonPropertyName("output_unavailable_channel_samples")] long OutputUnavailableChannelSamples,
    [property: JsonPropertyName("additional_unavailable_channel_samples")] long AdditionalUnavailableChannelSamples,
    [property: JsonPropertyName("intervals")] IReadOnlyList<ArtifactRemovalUnavailableInterval> Intervals)
{
    public void Validate(double startSeconds, double endSeconds, double samplingRateHz, IReadOnlyList<string> channels)
    {
        const double tolerance = 0.000001;
        if (SchemaVersion != "artifact-removal-validity-v1" || SamplingRateHz != samplingRateHz ||
            StartSample < 0 || EndSample <= StartSample ||
            Math.Abs(StartSample / SamplingRateHz - startSeconds) > tolerance ||
            Math.Abs(EndSample / SamplingRateHz - endSeconds) > tolerance ||
            Channels is null || Intervals is null || !Channels.SequenceEqual(channels))
            throw new InvalidOperationException("去伪迹可用性时间或通道契约无效。");
        long samples = 0, outputs = 0, additional = 0, previousEnd = StartSample;
        foreach (var row in Intervals)
        {
            if (row.StartSample < previousEnd || row.EndSample <= row.StartSample || row.EndSample > EndSample ||
                !double.IsFinite(row.StartSeconds) || !double.IsFinite(row.EndSeconds) ||
                Math.Abs(row.StartSeconds - row.StartSample / SamplingRateHz) > tolerance ||
                Math.Abs(row.EndSeconds - row.EndSample / SamplingRateHz) > tolerance ||
                row.MissingInputChannels is null || row.UnavailableOutputChannels is null || row.AdditionalUnavailableChannels is null ||
                row.UnavailableOutputChannels.Count == 0 ||
                row.UnavailableOutputChannels.Except(Channels).Any() ||
                row.MissingInputChannels.Except(Channels).Any() ||
                row.AdditionalUnavailableChannels.Except(row.UnavailableOutputChannels).Any() ||
                row.AdditionalUnavailableChannels.Intersect(row.MissingInputChannels).Any() ||
                row.Reason != (row.AdditionalUnavailableChannels.Count > 0 ? "missing_input_dependency" : "input_unavailable"))
                throw new InvalidOperationException("去伪迹不可用区间契约无效。");
            var count = row.EndSample - row.StartSample;
            samples += count;
            outputs += count * row.UnavailableOutputChannels.Count;
            additional += count * row.AdditionalUnavailableChannels.Count;
            previousEnd = row.EndSample;
        }
        if (samples != UnavailableSampleCount || outputs != OutputUnavailableChannelSamples || additional != AdditionalUnavailableChannelSamples)
            throw new InvalidOperationException("去伪迹不可用样本统计不一致。");
    }
}
