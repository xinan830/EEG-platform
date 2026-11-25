using System.Text.Json;
using System.Text.Json.Serialization;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.Services;
using BrainPlatform.Desktop.Modules.Projects.ViewModels;
using BrainPlatform.Desktop.Modules.Recordings.Access;
using BrainPlatform.Desktop.Modules.Review.Playback;

namespace BrainPlatform.Desktop.Modules.Projects.Api;

internal sealed class BackendRecordingAccessSession : IRecordingAccessSession
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IResearchProjectClient client;
    private readonly int sampleCount;
    private readonly IReadOnlyList<AcquisitionChannel> channels;
    private readonly IReadOnlyList<string> sourceChannelLabels;

    public BackendRecordingAccessSession(IResearchProjectClient client, BackendRecording recording,
        BackendResearchProject project)
    {
        this.client = client;
        var recordingId = Guid.ParseExact(recording.Id, "N");
        var rate = recording.SamplingRateHz ?? 0;
        var duration = recording.DurationSeconds ?? 0;
        if (!double.IsFinite(rate) || rate <= 0 || Math.Abs(rate - Math.Round(rate)) > 1e-9 ||
            !double.IsFinite(duration) || duration <= 0 || recording.Channels.Count == 0 ||
            recording.Channels.Count != recording.ChannelTypes.Count ||
            recording.Channels.Count != recording.ChannelUnits.Count ||
            recording.Channels.Any(string.IsNullOrWhiteSpace))
            throw new RecordingReadException("RECORDING_METADATA_INVALID", "导入记录的采样率、时长或通道信息无效。");
        var sfreq = checked((int)rate);
        var exactSampleCount = duration * rate;
        if (!double.IsFinite(exactSampleCount) || exactSampleCount < 1 ||
            exactSampleCount > int.MaxValue || Math.Abs(exactSampleCount - Math.Round(exactSampleCount)) > 1e-6)
            throw new RecordingReadException("RECORDING_METADATA_INVALID", "导入记录的采样范围无法精确映射到当前阅图时间轴。");
        sampleCount = checked((int)Math.Round(exactSampleCount));
        var binding = ReadImportBinding(recording);
        sourceChannelLabels = recording.Channels;
        channels = recording.Channels.Select((name, index) =>
        {
            var configured = binding.Profile?.Channels.FirstOrDefault(channel =>
                binding.NativeIndexes.TryGetValue(index, out var nativeIndex) && channel.NativeChannelIndex == nativeIndex);
            var isEeg = string.Equals(recording.ChannelTypes.ElementAtOrDefault(index), "eeg", StringComparison.OrdinalIgnoreCase);
            return new AcquisitionChannel(index, configured?.NativeChannelIndex ?? index,
                string.IsNullOrWhiteSpace(configured?.ElectrodeLabel) ? name : configured.ElectrodeLabel.Trim(),
                configured?.ExpectedKind ?? (isEeg ? AcquisitionChannelKind.Bipolar : AcquisitionChannelKind.Unknown),
                "V");
        }).ToArray();
        if (binding.Profile is not null &&
            EnabledEegChannelProjection.Project(binding.Profile, channels).Status != EegChannelProjectionStatus.Applied)
            throw new RecordingReadException("IMPORT_CHANNEL_BINDING_INVALID", "导入记录的通道配置快照与文件通道不匹配，无法安全恢复通道视图。");
        var createdAt = DateTimeOffset.TryParse(recording.CreatedAt, out var created) ? created : DateTimeOffset.UnixEpoch;
        Manifest = new LocalRawRecordingManifest(recordingId, "edf-bdf", "V", "imported", recording.Extension,
            sfreq, -1, createdAt, channels,
            new AcquisitionProjectContext(project.ProjectId, string.Empty, project.Name, string.Empty, string.Empty),
            ImportedHardwareConfiguration(recording));
        DurationSeconds = sampleCount / (double)sfreq;
        Metadata = new RecordingAccessMetadata(recordingId, recording.SourceSha256 ?? recording.Id, sfreq, 0,
            sampleCount - 1L, [], Manifest);
    }

    public LocalRawRecordingManifest Manifest { get; }
    public double DurationSeconds { get; }
    public RecordingAccessMetadata Metadata { get; }
    public long FirstSampleCounter => 0;
    public long LastSampleCounter => sampleCount - 1L;

    public async Task<RecordingAccessReadResult> ReadSamplesAsync(RecordingSampleRange range,
        IReadOnlyList<int>? channelIndexes, CancellationToken cancellationToken)
    {
        range.Validate();
        if (range.EndSampleExclusive > sampleCount)
            throw new RecordingReadException("SAMPLE_RANGE_INVALID", "读取范围超出导入记录时长。");
        var selected = channelIndexes?.ToArray() ?? Enumerable.Range(0, channels.Count).ToArray();
        if (selected.Length == 0 || selected.Any(index => index < 0 || index >= channels.Count) || selected.Distinct().Count() != selected.Length)
            throw new RecordingReadException("RECORDING_CHANNELS_INVALID", "请求的通道范围无效。");
        var outputChannels = selected.Select(index => channels[index]).ToArray();
        var rows = new List<RecordingReviewSegment>();
        var cursor = range.StartSample;
        var maxChunk = Math.Max(1, 500_000 / channels.Count);
        while (cursor < range.EndSampleExclusive)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = Math.Min(range.EndSampleExclusive, cursor + maxChunk);
            var block = await client.ReadRawSamplesAsync(Manifest.SessionId.ToString("N"), cursor, end, cancellationToken)
                .ConfigureAwait(false);
            ValidateBlock(block, cursor, end);
            AppendSegments(rows, block, selected);
            cursor = end;
        }
        var actual = new RecordingSampleRange(range.StartSample, range.EndSampleExclusive);
        var window = new RecordingReviewWindow(range.StartSample / (double)Metadata.SamplingRateHz,
            range.StartSample / (double)Metadata.SamplingRateHz, range.EndSampleExclusive / (double)Metadata.SamplingRateHz, rows);
        return new RecordingAccessReadResult(range, actual, outputChannels, window);
    }

    public long GetContiguousSegmentOrigin(long sampleCounter) => 0;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void ValidateBlock(BackendRawSampleRange block, long start, long end)
    {
        if (!string.Equals(block.RecordingId, Manifest.SessionId.ToString("N"), StringComparison.OrdinalIgnoreCase) ||
            block.StartSample != start || block.EndSample != end ||
            Math.Abs(block.SamplingRateHz - Metadata.SamplingRateHz) > 1e-9 ||
            !block.Channels.SequenceEqual(sourceChannelLabels, StringComparer.Ordinal) ||
            block.ChannelUnits.Count != channels.Count || block.ChannelUnits.Any(unit => unit != "V") ||
            block.ValuesVolts.Count != end - start || block.ValuesVolts.Any(row => row.Count != channels.Count))
            throw new RecordingReadException("RAW_SAMPLE_CONTRACT_INVALID", "后端返回的样本范围或通道契约与导入记录不一致。");

        if (block.UnavailableIntervals.Any(interval => interval.StartSample < start || interval.EndSample > end ||
                interval.EndSample <= interval.StartSample))
            throw new RecordingReadException("RAW_SAMPLE_GAP_INVALID", "后端返回的不可用样本区间无效。");
    }

    private static (ChannelConfigurationProfile? Profile, Dictionary<int, int> NativeIndexes) ReadImportBinding(
        BackendRecording recording)
    {
        var hasSnapshot = !string.IsNullOrWhiteSpace(recording.ImportChannelConfigurationSnapshotJson);
        var hasMapping = !string.IsNullOrWhiteSpace(recording.ImportChannelColumnMappingJson);
        if (!hasSnapshot && !hasMapping) return (null, []);
        if (!hasSnapshot || !hasMapping)
            throw new RecordingReadException("IMPORT_CHANNEL_BINDING_INVALID", "导入记录缺少完整的通道配置或列映射快照，无法安全恢复通道视图。");
        try
        {
            var profile = JsonSerializer.Deserialize<ChannelConfigurationProfile>(
                recording.ImportChannelConfigurationSnapshotJson!, JsonOptions);
            var mapping = JsonSerializer.Deserialize<List<ImportChannelColumnMapping>>(
                recording.ImportChannelColumnMappingJson!, JsonOptions);
            if (profile?.Channels is null || mapping is null)
                throw new RecordingReadException("IMPORT_CHANNEL_BINDING_INVALID", "导入记录的通道配置或列映射快照为空。");
            var nativeIndexes = mapping.ToDictionary(item => item.SourceColumn, item => item.NativeChannelIndex);
            var eegColumns = recording.ChannelTypes.Select((type, index) => (type, index))
                .Where(item => string.Equals(item.type, "eeg", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.index).ToArray();
            if (nativeIndexes.Count != mapping.Count || mapping.Any(item => item.SourceColumn < 0 ||
                    item.SourceColumn >= recording.Channels.Count || item.NativeChannelIndex < 0) ||
                mapping.Select(item => item.NativeChannelIndex).Distinct().Count() != mapping.Count ||
                !eegColumns.Order().SequenceEqual(nativeIndexes.Keys.Order()))
                throw new RecordingReadException("IMPORT_CHANNEL_BINDING_INVALID", "导入记录的列映射与文件 EEG 通道不一致。");
            foreach (var (_, nativeIndex) in nativeIndexes)
            {
                var configured = profile.Channels.SingleOrDefault(item => item.NativeChannelIndex == nativeIndex);
                if (configured is null || configured.ExpectedKind is not (AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar))
                    throw new RecordingReadException("IMPORT_CHANNEL_BINDING_INVALID", "导入记录的通道配置索引或通道类型无效。");
            }
            return (profile, nativeIndexes);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new RecordingReadException("IMPORT_CHANNEL_BINDING_INVALID", "导入记录的通道配置或列映射快照格式无效。", exception);
        }
    }

    private static IReadOnlyDictionary<string, string> ImportedHardwareConfiguration(BackendRecording recording)
    {
        var hardware = new Dictionary<string, string>();
        Add("channel_configuration_snapshot_json", recording.ImportChannelConfigurationSnapshotJson);
        Add("acquisition_montage_snapshot_json", recording.ImportAcquisitionMontageSnapshotJson);
        Add("montage_configuration_snapshot_json", recording.ImportMontageConfigurationSnapshotJson);
        Add("montage_catalogue_snapshot_json", recording.ImportMontageCatalogueSnapshotJson);
        return hardware;

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) hardware[key] = value;
        }
    }

    private sealed record ImportChannelColumnMapping(
        [property: JsonPropertyName("source_column")] int SourceColumn,
        [property: JsonPropertyName("native_channel_index")] int NativeChannelIndex);

    private static void AppendSegments(List<RecordingReviewSegment> destination, BackendRawSampleRange block,
        IReadOnlyList<int> selected)
    {
        var values = new double[block.ValuesVolts.Count * selected.Count];
        for (var row = 0; row < block.ValuesVolts.Count; row++)
        for (var column = 0; column < selected.Count; column++)
            values[row * selected.Count + column] = block.ValuesVolts[row][selected[column]] ?? double.NaN;

        var gaps = block.UnavailableIntervals.Where(interval => interval.Reason == "source_gap")
            .Select(interval => (Start: Math.Max(interval.StartSample, block.StartSample), End: Math.Min(interval.EndSample, block.EndSample)))
            .Where(interval => interval.End > interval.Start).OrderBy(interval => interval.Start).ToArray();
        var start = block.StartSample;
        foreach (var gap in gaps)
        {
            AddRange(destination, values, start, gap.Start, block.StartSample, selected.Count);
            start = Math.Max(start, gap.End);
        }
        AddRange(destination, values, start, block.EndSample, block.StartSample, selected.Count);
    }

    private static void AddRange(List<RecordingReviewSegment> destination, double[] values, long start, long end,
        long blockStart, int channelCount)
    {
        if (end <= start) return;
        var offset = checked((int)((start - blockStart) * channelCount));
        var length = checked((int)((end - start) * channelCount));
        var segment = new RecordingReviewSegment(start, checked((int)(end - start)), channelCount,
            values.AsSpan(offset, length).ToArray());
        if (destination.Count > 0 && destination[^1].LastSampleCounter + 1 == segment.FirstSampleCounter)
        {
            var previous = destination[^1];
            destination[^1] = previous with
            {
                SampleCount = checked(previous.SampleCount + segment.SampleCount),
                SampleMajorValues = [.. previous.SampleMajorValues, .. segment.SampleMajorValues],
            };
            return;
        }
        destination.Add(segment);
    }
}
