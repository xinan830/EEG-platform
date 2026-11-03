using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using System.Net.Http;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

public sealed partial class HttpAlgorithmClient
{
    public Task<Topomap3DLocationResponse> ResolveTopomap3DLocationsAsync(
        Topomap3DLocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Channels.Count is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(request), "三维电极位置解析必须包含 1 到 256 个通道。");
        return SendAsync<Topomap3DLocationResponse>(HttpMethod.Post,
            "api/topomap/3d/locations", request, cancellationToken);
    }

    public Task<Topomap3DResponse> GetRunTopomap3DAsync(string runId, long cursorSample,
        CancellationToken cancellationToken, string powerType = "relative", string? bandId = null)
    {
        Validate3DSelection(cursorSample, powerType, bandId);
        var query = $"cursor_sample={cursorSample}&power_type={Uri.EscapeDataString(powerType)}";
        if (bandId is not null) query += $"&band_id={Uri.EscapeDataString(bandId)}";
        return SendAsync<Topomap3DResponse>(HttpMethod.Get,
            $"api/runs/{Uri.EscapeDataString(RequireId(runId))}/topomap/3d?{query}", null, cancellationToken);
    }

    public Task<Topomap3DResponse> GetDynamicTopomap3DAsync(string windowId, string? projectId,
        CancellationToken cancellationToken, string powerType = "relative", string? bandId = null)
    {
        Validate3DSelection(0, powerType, bandId);
        var query = $"power_type={Uri.EscapeDataString(powerType)}";
        if (!string.IsNullOrWhiteSpace(projectId)) query += $"&project_id={Uri.EscapeDataString(projectId)}";
        if (bandId is not null) query += $"&band_id={Uri.EscapeDataString(bandId)}";
        return SendAsync<Topomap3DResponse>(HttpMethod.Get,
            $"api/analysis-windows/{Uri.EscapeDataString(RequireId(windowId))}/topomap/3d?{query}", null, cancellationToken);
    }

    private static void Validate3DSelection(long cursorSample, string powerType, string? bandId)
    {
        if (cursorSample < 0) throw new ArgumentOutOfRangeException(nameof(cursorSample));
        if (powerType is not ("relative" or "absolute")) throw new ArgumentOutOfRangeException(nameof(powerType));
        if (bandId is not (null or "delta" or "theta" or "alpha" or "beta" or "gamma"))
            throw new ArgumentOutOfRangeException(nameof(bandId));
    }
}
