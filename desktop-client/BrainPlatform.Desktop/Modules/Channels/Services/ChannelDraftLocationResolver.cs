using BrainPlatform.Desktop.Modules.Channels.Api;
using BrainPlatform.Desktop.Modules.Channels.Domain;

namespace BrainPlatform.Desktop.Modules.Channels.Services;

/// <summary>
/// Resolves standard colin27_1020 electrode coordinates for draft EEG rows,
/// discarding stale results when a row label changes while the request is in flight.
/// </summary>
internal sealed class ChannelDraftLocationResolver(IElectrodeLocationClient? electrodeLocationClient)
{
    public async Task ResolveAsync(
        IEnumerable<ChannelConfigurationDraftRow> draftRows,
        CancellationToken cancellationToken)
    {
        if (electrodeLocationClient is null)
        {
            throw new InvalidOperationException("标准位置解析服务尚未连接，请确认后端已启动。通道配置仍可正常使用。");
        }

        var rows = draftRows.Where(row => row.IsEegInput).ToArray();
        var labels = rows.Select(row => row.ElectrodeLabel).ToArray();
        var revisions = rows.Select(row => row.ElectrodeLabelRevision).ToArray();
        var results = await electrodeLocationClient.ResolveAsync(labels, cancellationToken);
        if (results.Count != rows.Length)
        {
            throw new InvalidOperationException("标准位置解析响应数量与请求通道数不一致。");
        }

        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var result = results[index];
            if (!string.Equals(result.RequestedLabel, labels[index], StringComparison.Ordinal))
            {
                throw new InvalidOperationException("标准电极位置解析响应的通道顺序与请求不一致。");
            }
            if (result.Status is not ("available" or "unavailable"))
            {
                throw new InvalidOperationException("标准电极位置解析响应包含未知状态。");
            }
            if (row.ElectrodeLabelRevision != revisions[index] ||
                !string.Equals(row.ElectrodeLabel, labels[index], StringComparison.Ordinal))
            {
                continue;
            }
            row.SetElectrodeLocation(result.Status == "available" &&
                                     result.CanonicalSite is not null &&
                                     result.Coordinates is { Length: 3 } xyz &&
                                     result.MapCoordinates is { Length: 2 } map &&
                                     map.All(double.IsFinite) && map.All(value => value is >= -1 and <= 1) &&
                                     result.CoordinateFrame == "mri" && result.CoordinateUnit == "m"
                ? new ElectrodeLocationSnapshot(
                    result.CanonicalSite, xyz[0], xyz[1], xyz[2], "mri", "m", result.SourceId, result.SourceVersion,
                    map[0], map[1])
                : null);
        }
    }
}
