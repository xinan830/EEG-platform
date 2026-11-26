using System.Net;
using System.Net.Http.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using Xunit;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class HttpArtifactReferenceTests
{
    [Fact]
    public async Task ReferenceCatalogueAndIcaRequest_KeepExactSourceNames()
    {
        var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new HttpAlgorithmClient(http);
        var catalogue = await client.GetArtifactReferenceChannelsAsync("rec-1");
        Assert.Equal("EOG + 左", Assert.Single(catalogue.Channels).Name);
        var saved = await client.SaveChannelIdentitiesAsync("rec-1", new ChannelIdentityUpdate("source-hash",
            [new("EOG + 左", "eog")]));
        Assert.Equal("eog", saved.Channels[0].Identity);
        Assert.Equal("source-hash", handler.Body!.RootElement.GetProperty("source_sha256").GetString());
        Assert.Equal("eog", handler.Body.RootElement.GetProperty("channels")[0].GetProperty("identity").GetString());
        await client.InspectIcaComponentsAsync("rec-1", eogChannels: ["EOG + 左", "EOG2"],
            ecgChannels: ["ECG + 右"], allowFrontalEogProxy: false);
        var query = Uri.UnescapeDataString(handler.LastUri!.Query);
        Assert.Contains("eog_channels=EOG + 左,EOG2", query);
        Assert.Contains("ecg_channels=ECG + 右", query);
        Assert.Contains("allow_frontal_eog_proxy=false", query);
        var payload = new OfflinePreprocessingConfigPayload(new("none", []), new("none", null, null),
            new("ica_infomax", ["EOG + 左"], false, 3, 0.7, [], "fit", EcgChannels: ["ECG + 右"]));
        var layout = await client.GetPreprocessingAnalysisChannelsAsync("rec-1", payload, "CAR + 原始");
        Assert.Equal(new[] { "F3-AVG" }, layout.Channels);
        Assert.Contains("montage_id=CAR + 原始", Uri.UnescapeDataString(handler.LastUri!.Query));
        Assert.Equal("ECG + 右", handler.Body!.RootElement.GetProperty("artifact_removal")
            .GetProperty("ecg_channels")[0].GetString());
    }

    private sealed class Handler : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public System.Text.Json.JsonDocument? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            LastUri = request.RequestUri;
            if (request.Content is not null)
                Body = System.Text.Json.JsonDocument.Parse(await request.Content.ReadAsStringAsync(token));
            if (LastUri!.AbsolutePath.EndsWith("channel-identities", StringComparison.Ordinal)) Assert.Equal(HttpMethod.Put, request.Method);
            object response = LastUri!.AbsolutePath.EndsWith("reference-channels", StringComparison.Ordinal) ||
                              LastUri.AbsolutePath.EndsWith("channel-identities", StringComparison.Ordinal)
                ? new ArtifactReferenceCatalogue("rec-1", 500, 30000, [new("EOG + 左", "V", "eog")])
                : LastUri.AbsolutePath.EndsWith("analysis-channels", StringComparison.Ordinal)
                ? new PreprocessingAnalysisChannels("rec-1", ["F3-AVG"])
                : new { recording_id = "rec-1", method = "ica_infomax", components = Array.Empty<object>() };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) };
        }
    }
}
