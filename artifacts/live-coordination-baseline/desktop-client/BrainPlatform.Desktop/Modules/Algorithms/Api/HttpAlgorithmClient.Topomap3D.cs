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

}
