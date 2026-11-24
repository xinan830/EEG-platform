using System.Net.Http;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

public sealed partial class HttpAlgorithmClient
{
    public Task<CustomFrequencyBinsResponse> GetCustomFrequencyBinsAsync(
        CustomFrequencyWindowPairRequest request, CancellationToken cancellationToken) =>
        SendAsync<CustomFrequencyBinsResponse>(HttpMethod.Post,
            "api/analysis-windows/custom-frequency/bins", request, cancellationToken);

    public Task<CustomFrequencyRangeResponse> GetCustomFrequencyRangeAsync(
        CustomFrequencyRangeRequest request, CancellationToken cancellationToken) =>
        SendAsync<CustomFrequencyRangeResponse>(HttpMethod.Post,
            "api/analysis-windows/custom-frequency/range", request, cancellationToken);
}
