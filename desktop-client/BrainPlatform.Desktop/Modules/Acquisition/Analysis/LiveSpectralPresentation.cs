using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;
using BrainPlatform.Desktop.Modules.Acquisition.Runtime;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

internal static class LiveSpectralPresentation
{
    internal static IReadOnlyList<string> AnalysisChannelNames(AcquisitionStreamMetadata stream) =>
        stream.Channels
            .Where(channel => channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
            .Where(channel => !string.IsNullOrWhiteSpace(channel.Label))
            .Select(ChannelName)
            .ToArray();

    internal static IReadOnlyList<string> AnalysisChannelNames(LiveMontageSnapshot montage) => montage.OrderedChannelNames;

    internal static string ChannelName(AcquisitionChannel channel) =>
        channel.Label is { } label && !string.IsNullOrWhiteSpace(label)
            ? label.Trim()
            : $"channel-{channel.StreamIndex}";

    internal static JsonElement MapConfig(AcquisitionStreamMetadata stream, IReadOnlyList<string> fieldIds)
    {
        var channels = stream.Channels
            .Where(channel => channel.Kind == AcquisitionChannelKind.Reference)
            .Where(channel => !string.IsNullOrWhiteSpace(channel.Label))
            .ToArray();
        if (channels.Length == 0) throw new InvalidOperationException("当前采集会话没有可用于地形图的单电极通道。");

        var positions = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (stream.HardwareConfiguration.TryGetValue("channel_configuration_snapshot_json", out var snapshot) &&
            JsonSerializer.Deserialize<ChannelConfigurationProfile>(snapshot) is { } profile)
        {
            var byIndex = profile.Channels.ToDictionary(channel => channel.NativeChannelIndex);
            foreach (var channel in channels)
            {
                var name = ChannelName(channel);
                if (!byIndex.TryGetValue(channel.NativeChannelIndex, out var entry) ||
                    entry.ExpectedKind != AcquisitionChannelKind.Reference ||
                    !string.Equals(name, entry.ElectrodeLabel.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                positions.Add(name, new
                {
                    x = entry.ManualMapPosition?.X ?? entry.ElectrodeLocation?.MapX,
                    y = entry.ManualMapPosition?.Y ?? entry.ElectrodeLocation?.MapY,
                    enabled = entry.IsSelectedForDisplay,
                });
            }
        }
        return JsonSerializer.SerializeToElement(new
        {
            spectrum = LiveRbpPresentation.Config(channels.Select(ChannelName).ToArray()),
            field_ids = fieldIds,
            positions,
        });
    }

    internal static JsonElement MapConfig(LiveMontageSnapshot montage, IReadOnlyList<string> fieldIds)
    {
        var sourceChannels = montage.Profile.ChannelConfigurationSnapshot.Channels
            .Where(channel => channel.IsSelectedForDisplay &&
                channel.ExpectedKind == AcquisitionChannelKind.Reference &&
                !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .Select(channel => channel with { ElectrodeLabel = channel.ElectrodeLabel.Trim() })
            .ToArray();
        var duplicate = sourceChannels
            .GroupBy(channel => channel.ElectrodeLabel, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"实时导联配置包含重复电极名“{duplicate.Key}”。");
        }
        var source = sourceChannels.ToDictionary(channel => channel.ElectrodeLabel, StringComparer.OrdinalIgnoreCase);
        var positions = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var channel in montage.Profile.DerivedChannels.OrderBy(channel => channel.DisplayOrder))
        {
            if (!source.TryGetValue(channel.PositiveLabel.Trim(), out var electrode) ||
                electrode.ExpectedKind != AcquisitionChannelKind.Reference ||
                (electrode.ManualMapPosition is null && electrode.ElectrodeLocation is null)) continue;
            var position = electrode.ManualMapPosition;
            var location = electrode.ElectrodeLocation;
            positions[channel.Name] = new
            {
                x = position?.X ?? location?.MapX,
                y = position?.Y ?? location?.MapY,
                enabled = electrode.IsSelectedForDisplay,
            };
        }
        if (positions.Count == 0) throw new InvalidOperationException("当前实时导联没有可用于地形图的电极位置。");
        return JsonSerializer.SerializeToElement(new
        {
            spectrum = LiveRbpPresentation.Config(positions.Keys.ToArray()),
            field_ids = fieldIds,
            positions,
        });
    }

    internal static JsonElement MapConfig(AcquisitionStreamMetadata stream, string fieldId) =>
        MapConfig(stream, [fieldId]);

    internal static JsonElement RbpTopomapConfig(AcquisitionStreamMetadata stream, LiveMontageSnapshot? montage,
        IReadOnlyList<string> fieldIds) => montage is { }
            ? MapConfig(montage, fieldIds)
            : MapConfig(stream, fieldIds);

    internal static JsonElement ReadSpectrum(LiveAnalysisUpdate update, string channel, string unit)
    {
        var payload = update.Payload;
        if (payload.GetProperty("schema_version").GetString() != "live-spectrum-result-v1" ||
            payload.GetProperty("unit").GetString() != unit ||
            !payload.GetProperty("channels").EnumerateArray().Select(item => item.GetString())
                .SequenceEqual([channel], StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("实时频谱单位、版本或通道不匹配。");
        return payload;
    }

    internal static void ApplyPsd(PsdPreviewState state, LiveAnalysisUpdate update, string channel)
    {
        var payload = ReadSpectrum(update, channel, "V^2/Hz");
        var frequencies = payload.GetProperty("frequencies_hz").EnumerateArray().ToArray();
        var values = payload.GetProperty("values").EnumerateArray().Single().EnumerateArray().ToArray();
        if (values.Length != frequencies.Length || frequencies.Length == 0 ||
            frequencies.Any(item => item.ValueKind != JsonValueKind.Number || !double.IsFinite(item.GetDouble()) ||
                item.GetDouble() < 0 || item.GetDouble() > update.SamplingRateHz / 2d) ||
            frequencies.Zip(frequencies.Skip(1)).Any(pair => pair.First.GetDouble() >= pair.Second.GetDouble()) ||
            values.Any(item => item.ValueKind != JsonValueKind.Null &&
                (item.ValueKind != JsonValueKind.Number || !double.IsFinite(item.GetDouble() * 1e12) || item.GetDouble() < 0)))
            throw new InvalidOperationException("实时功率谱密度矩阵或频率轴无效。");
        PsdPointProjection.Apply(state, frequencies, values, convertFromVolts: true);
        if (!state.HasPreview) throw new InvalidOperationException("实时功率谱密度没有可显示的数值。");
    }

    internal static StftPreview ReadStft(LiveAnalysisUpdate update, string channel, TimeRange range) =>
        StftPreview.FromLiveSpectrum(ReadSpectrum(update, channel, "dB re 1 uV^2/Hz"), range, update.SamplingRateHz);

    internal static IReadOnlyList<TopomapField> ReadMaps(LiveAnalysisUpdate update, IReadOnlyList<string> fieldIds)
    {
        var payload = update.Payload;
        if (payload.GetProperty("schema_version").GetString() != "live-topomap-result-v1")
            throw new InvalidOperationException("实时地形图字段、版本或单位不匹配。");
        var fields = payload.TryGetProperty("fields", out var fieldArray)
            ? fieldArray.Deserialize<List<TopomapField>>() ?? []
            : payload.GetProperty("field").Deserialize<TopomapField>() is { } legacy ? [legacy] : [];
        if (fields.Count != fieldIds.Count)
            throw new InvalidOperationException("实时地形图字段数量不匹配。");
        foreach (var pair in fields.Zip(fieldIds))
        {
            var fieldId = pair.Second;
            var field = pair.First;
            var unit = fieldId.StartsWith("psd.", StringComparison.Ordinal) ? "V^2/Hz" :
                fieldId.StartsWith("rbp.relative.", StringComparison.Ordinal) ? "ratio" :
                fieldId.StartsWith("rbp.absolute.", StringComparison.Ordinal) ? "uV^2" : "uV";
            if (field.FieldId != fieldId || field.Unit != unit || field.Status != update.State ||
                field.Points is null || field.IncludedChannels is null || field.ExcludedChannels is null ||
                (field.Status == "ready" && field.Grid is null))
                throw new InvalidOperationException("实时地形图字段、版本或单位不匹配。");
        }
        return fields;
    }

    internal static TopomapField ReadMap(LiveAnalysisUpdate update, string fieldId) =>
        ReadMaps(update, [fieldId]).Single();
}
