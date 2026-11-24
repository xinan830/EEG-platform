using System.Globalization;
using System.Net.Http;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

public sealed partial class HttpAlgorithmClient
{
    public Task<TnkWorkbenchSummary> GetTnkWorkbenchSummaryAsync(string runId, double cursorSeconds, CancellationToken token) =>
        SendAsync<TnkWorkbenchSummary>(HttpMethod.Get,
            $"api/runs/{Uri.EscapeDataString(RequireId(runId))}/tnk-workbench?cursor_s={cursorSeconds.ToString("R", CultureInfo.InvariantCulture)}", null, token);
}
