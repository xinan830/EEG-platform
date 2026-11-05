using System.IO;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Events.Domain;
using BrainPlatform.Desktop.Modules.Recordings.Access;
using BrainPlatform.Desktop.Modules.Review.Playback;
using SharpEDFlib;

namespace BrainPlatform.Desktop.Modules.Review.Export;

public static class RecordingReviewSignalExporter
{
    private const double VoltsToMicrovolts = 1_000_000d;
    private const int MaximumLabelLength = 16;

    public static async Task ExportAsync(
        string targetPath,
        EDFileType format,
        LocalRawRecordingManifest manifest,
        DateTimeOffset recordingStartUtc,
        long firstSampleCounter,
        long recordingStartSample,
        RecordingAccessReadResult data,
        IReadOnlyList<RecordingEvent> events,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        if (format is not (EDFileType.EDFPlus or EDFileType.BDFPlus))
            throw new ArgumentOutOfRangeException(nameof(format), "仅支持 EDF 或 BDF 格式。");

        var signalChannels = manifest.Channels
            .Where(channel => channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
            .OrderBy(channel => channel.StreamIndex)
            .ToArray();
        if (signalChannels.Length == 0)
            throw new InvalidDataException("记录中没有可导出的脑电通道。");
        if (!string.Equals(manifest.EegSignalUnit, "V", StringComparison.OrdinalIgnoreCase) ||
            signalChannels.Any(channel => !string.Equals(channel.Unit, "V", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("导出要求所有脑电源通道单位均为 V；当前记录包含不支持的单位。");
        var requested = data.RequestedRange;
        if (data.ActualRange != requested || data.Window.Segments.Count != 1)
            throw new InvalidDataException("整段录音包含采样缺口，不能导出为连续 EDF/BDF 文件。");
        if (checked(data.ActualRange.StartSample - firstSampleCounter) != recordingStartSample)
            throw new InvalidDataException("导出区间与录音时间原点不一致，未生成导出文件。");
        if (!data.Channels.Select(channel => channel.StreamIndex).SequenceEqual(
                signalChannels.Select(channel => channel.StreamIndex)))
            throw new InvalidDataException("读取到的源通道顺序与记录清单不一致，未生成导出文件。");

        var segment = data.Window.Segments[0];
        if (segment.FirstSampleCounter != requested.StartSample ||
            segment.SampleCount != requested.Length ||
            segment.ChannelCount != signalChannels.Length)
            throw new InvalidDataException("读取到的原始样本范围或通道数不完整，未生成导出文件。");

        var tempPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(targetPath))!,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        var channelMappings = data.Channels.Select((channel, index) => new ChannelLabelMapping(
                index + 1,
                IsStandardLabel(channel.Label) ? channel.Label! : $"CH{index + 1:D4}",
                channel.StreamIndex,
                channel.NativeChannelIndex,
                channel.Kind.ToString(),
                channel.Unit,
                channel.Label))
            .ToArray();
        var labelMapPath = targetPath + ".labels.json";
        var tempLabelMapPath = tempPath + ".labels.json";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => WriteFile(tempPath, format, manifest, recordingStartUtc,
                firstSampleCounter, recordingStartSample, data, events, cancellationToken), cancellationToken);
            await using (var metadataFile = File.Create(tempLabelMapPath))
                await JsonSerializer.SerializeAsync(metadataFile, new ExportMetadata(
                    "recording-review-export-metadata-v1", manifest.SessionId,
                    manifest.SamplingRateHz, recordingStartUtc,
                    manifest.HardwareConfiguration.Where(pair => pair.Key is not "serial_number" and not "device_instance_id")
                        .ToDictionary(pair => pair.Key, pair => pair.Value),
                    channelMappings), cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, targetPath, true);
            File.Move(tempLabelMapPath, labelMapPath, true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempLabelMapPath)) File.Delete(tempLabelMapPath);
        }
    }

    private static void WriteFile(
        string tempPath,
        EDFileType format,
        LocalRawRecordingManifest manifest,
        DateTimeOffset recordingStartUtc,
        long firstSampleCounter,
        long recordingStartSample,
        RecordingAccessReadResult data,
        IReadOnlyList<RecordingEvent> events,
        CancellationToken cancellationToken)
    {
        var samples = data.Window.Segments[0];
        var signalCount = data.Channels.Count;
        var digitalLimit = format == EDFileType.BDFPlus ? 8_388_607 : 32_767;
        var samplesPerDataRecord = ResolveSamplesPerDataRecord(samples.SampleCount, manifest.SamplingRateHz);
        var minima = Enumerable.Repeat(double.PositiveInfinity, signalCount).ToArray();
        var maxima = Enumerable.Repeat(double.NegativeInfinity, signalCount).ToArray();

        for (var sampleIndex = 0; sampleIndex < samples.SampleCount; sampleIndex++)
        {
            if ((sampleIndex & 0x3FFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (var channelIndex = 0; channelIndex < signalCount; channelIndex++)
            {
                var value = samples.SampleMajorValues[sampleIndex * signalCount + channelIndex] * VoltsToMicrovolts;
                minima[channelIndex] = Math.Min(minima[channelIndex], value);
                maxima[channelIndex] = Math.Max(maxima[channelIndex], value);
            }
        }

        var exportStart = recordingStartUtc.AddSeconds(recordingStartSample / (double)manifest.SamplingRateHz).UtcDateTime;
        using var writer = new EDFWriter(tempPath, format, signalCount);
        writer.SetDataRecordDurationIn10Microseconds(checked((int)Math.Round(
            samplesPerDataRecord * 100_000d / manifest.SamplingRateHz)));
        writer.SetStartDateTime(exportStart);
        writer.SetStartDateTimeSubsecond((int)(exportStart.Ticks % TimeSpan.TicksPerSecond));
        var firstSampleRelative = recordingStartSample;
        var endSampleRelative = checked(recordingStartSample + data.ActualRange.Length);
        var includedEvents = events.Where(item => item.IsDisplayable &&
            item.StartSample >= firstSampleRelative && item.StartSample < endSampleRelative).ToArray();
        for (var channelIndex = 0; channelIndex < signalCount; channelIndex++)
        {
            writer.SetSampleFrequency(channelIndex, samplesPerDataRecord);
            var sourceLabel = data.Channels[channelIndex].Label;
            var exportLabel = IsStandardLabel(sourceLabel)
                ? sourceLabel!
                : $"CH{channelIndex + 1:D4}";
            writer.SetLabel(channelIndex, exportLabel);
            writer.SetPhysicalDimension(channelIndex, "uV");
            writer.SetDigitalMinimum(channelIndex, format == EDFileType.BDFPlus ? -8_388_608 : -32_768);
            writer.SetDigitalMaximum(channelIndex, digitalLimit);
            var minimum = minima[channelIndex];
            var maximum = maxima[channelIndex];
            if (minimum == maximum)
            {
                minimum -= 0.5;
                maximum += 0.5;
            }
            writer.SetPhysicalMinimum(channelIndex, minimum);
            writer.SetPhysicalMaximum(channelIndex, maximum);
        }

        var physicalSamples = new double[signalCount, samplesPerDataRecord];
        var recordCount = samples.SampleCount / samplesPerDataRecord;
        for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var firstSample = recordIndex * samplesPerDataRecord;
            for (var sampleIndex = 0; sampleIndex < samplesPerDataRecord; sampleIndex++)
            {
                var sourceOffset = checked((firstSample + sampleIndex) * signalCount);
                for (var channelIndex = 0; channelIndex < signalCount; channelIndex++)
                    physicalSamples[channelIndex, sampleIndex] =
                        samples.SampleMajorValues[sourceOffset + channelIndex] * VoltsToMicrovolts;
            }
            writer.WritePhysicalSamplesBlock(physicalSamples);
        }
        foreach (var item in includedEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var onsetUs = checked((long)Math.Round(
                (item.StartSample - firstSampleRelative) * 1_000_000d / manifest.SamplingRateHz));
            var durationSamples = Math.Min(item.DurationSamples, endSampleRelative - item.StartSample);
            var durationUs = checked((long)Math.Round(durationSamples * 1_000_000d / manifest.SamplingRateHz));
            writer.WriteAnnotationUTF8(item.DefinitionSnapshot.Name, onsetUs, durationUs);
        }
        writer.Close();
    }

    private static bool IsStandardLabel(string? label) =>
        !string.IsNullOrWhiteSpace(label) &&
        label.Length <= MaximumLabelLength &&
        label.All(character => character is >= ' ' and <= '~');

    private static int ResolveSamplesPerDataRecord(int totalSamples, int samplingRateHz)
    {
        var maximum = Math.Min(totalSamples, checked(samplingRateHz * 60));
        var minimum = Math.Max(1, (samplingRateHz + 999) / 1000);
        for (var candidate = maximum; candidate >= minimum; candidate--)
            if (totalSamples % candidate == 0) return candidate;
        throw new InvalidDataException("整段录音无法按 EDF/BDF 数据记录规则无损分段。");
    }

    private sealed record ChannelLabelMapping(
        int ChannelNumber,
        string ExportLabel,
        int StreamIndex,
        int NativeChannelIndex,
        string Kind,
        string Unit,
        string? SourceLabel);

    private sealed record ExportMetadata(
        string SchemaVersion,
        Guid RecordingId,
        int SamplingRateHz,
        DateTimeOffset RecordingStartUtc,
        IReadOnlyDictionary<string, string> HardwareConfiguration,
        IReadOnlyList<ChannelLabelMapping> ChannelMappings);
}
