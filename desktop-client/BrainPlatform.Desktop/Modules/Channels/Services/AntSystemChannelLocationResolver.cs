using BrainPlatform.Desktop.Modules.Channels.Api;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using System.Net.Http;

namespace BrainPlatform.Desktop.Modules.Channels.Services;

/// <summary>Attaches authoritative standard-template positions to the generated ANT preset.</summary>
public static class AntSystemChannelLocationResolver
{
    public static string AppendWarning(string status, string? warning) => warning is null
        ? status
        : $"{status} {warning}";

    public static async Task<(ChannelConfigurationProfile Profile, string? Warning)> TryResolveAsync(
        ChannelConfigurationProfile profile,
        IElectrodeLocationClient? client,
        CancellationToken cancellationToken)
    {
        if (client is null)
        {
            return (profile, "标准电极位置服务尚未连接，ANT 通道配置可用，但位置暂不可用。");
        }

        try
        {
            return (await ResolveAsync(profile, client, cancellationToken), null);
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException
            or InvalidOperationException or TaskCanceledException)
        {
            return (profile, $"ANT 通道配置仍可使用，但标准电极位置解析失败：{exception.Message}");
        }
    }

    public static async Task<ChannelConfigurationProfile> ResolveAsync(
        ChannelConfigurationProfile profile,
        IElectrodeLocationClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(client);
        if (profile.Source != ChannelConfigurationSource.System ||
            !string.Equals(profile.Id, "system-ant-standard-28-input", StringComparison.Ordinal))
        {
            throw new ArgumentException("只能解析 ANT 默认系统通道配置的位置。", nameof(profile));
        }

        var channels = profile.Channels
            .Where(channel => !string.IsNullOrWhiteSpace(channel.ElectrodeLabel) &&
                !string.Equals(channel.ElectrodeLabel.Trim(), "REF", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(channel.ElectrodeLabel.Trim(), "GND", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var labels = channels.Select(channel => channel.ElectrodeLabel.Trim()).ToArray();
        var results = await client.ResolveAsync(labels, cancellationToken);
        if (results.Count != channels.Length)
        {
            throw new InvalidOperationException("ANT 标准位置解析响应数量与请求通道数不一致。");
        }

        var resolved = profile.Channels.ToDictionary(channel => channel.NativeChannelIndex);
        for (var index = 0; index < channels.Length; index++)
        {
            var channel = channels[index];
            var result = results[index];
            if (!string.Equals(result.RequestedLabel, labels[index], StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ANT 标准位置解析响应的通道顺序与请求不一致。");
            }

            var location = result.Status switch
            {
                "unavailable" => null,
                "available" => CreateSnapshot(result),
                _ => throw new InvalidOperationException("ANT 标准位置解析响应包含未知状态。"),
            };
            resolved[channel.NativeChannelIndex] = channel with { ElectrodeLocation = location };
        }

        return profile with
        {
            Channels = profile.Channels.Select(channel => resolved[channel.NativeChannelIndex]).ToArray()
        };
    }

    private static ElectrodeLocationSnapshot CreateSnapshot(ElectrodeLocationResolution result)
    {
        if (string.IsNullOrWhiteSpace(result.CanonicalSite) || result.Coordinates is not { Length: 3 } xyz ||
            result.MapCoordinates is not { Length: 2 } map ||
            result.CoordinateFrame != "mri" || result.CoordinateUnit != "m" ||
            result.SourceId != "mne.standard_1020" || string.IsNullOrWhiteSpace(result.SourceVersion) ||
            xyz.Any(value => !double.IsFinite(value)) || map.Any(value => !double.IsFinite(value)) ||
            map.Any(value => value is < -1 or > 1))
        {
            throw new InvalidOperationException("ANT 标准位置解析响应不符合坐标来源、单位或数值契约。");
        }

        return new ElectrodeLocationSnapshot(result.CanonicalSite, xyz[0], xyz[1], xyz[2],
            result.CoordinateFrame, result.CoordinateUnit, result.SourceId, result.SourceVersion, map[0], map[1]);
    }
}
