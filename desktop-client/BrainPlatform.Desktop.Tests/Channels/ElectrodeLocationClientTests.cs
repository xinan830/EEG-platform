using System.Net;
using System.Text;
using BrainPlatform.Desktop.Modules.Channels.Api;

namespace BrainPlatform.Desktop.Tests.Channels;

public sealed class ElectrodeLocationClientTests
{
    [Fact]
    public async Task ResolveAsync_PostsLabelsAndReadsTypedLocationResponse()
    {
        var handler = new StubHandler((request, body) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/api/electrode-locations/resolve", request.RequestUri!.AbsolutePath);
            Assert.Equal("{\"labels\":[\"Fp1\",\"REF\"]}", body);
            return JsonResponse("""{"montage":"colin27_1020","locations":[{"requested_label":"Fp1","status":"available","canonical_site":"Fp1","coordinates":[-0.03,0.08,-0.007],"map_coordinates":[-0.24,-0.87],"coordinate_frame":"mri","coordinate_unit":"m","source_id":"mne.colin27_1020","source_version":"1.12.1"},{"requested_label":"REF","status":"unavailable","canonical_site":null,"coordinates":null,"map_coordinates":null,"coordinate_frame":null,"coordinate_unit":null,"source_id":"mne.colin27_1020","source_version":"1.12.1"}]}""");
        });
        var client = new HttpElectrodeLocationClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        });

        var locations = await client.ResolveAsync(["Fp1", "REF"], CancellationToken.None);

        Assert.Equal(2, locations.Count);
        Assert.Equal("Fp1", locations[0].CanonicalSite);
        Assert.Equal(new[] { -0.24, -0.87 }, locations[0].MapCoordinates);
        Assert.Equal("unavailable", locations[1].Status);
    }

    [Fact]
    public async Task ResolveAsync_UsesStructuredMessageWhenBackendRejectsRequest()
    {
        var client = new HttpElectrodeLocationClient(new HttpClient(
            new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent("""{"code":"ELECTRODE_LOCATION_SOURCE_UNAVAILABLE","message":"MNE montage unavailable","request_id":"test"}""")
            }))
        {
            BaseAddress = new Uri("http://localhost/")
        });

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.ResolveAsync(["Cz"], CancellationToken.None));

        Assert.Equal("MNE montage unavailable", error.Message);
    }

    private static StringContent JsonContent(string value) => new(value, Encoding.UTF8, "application/json");

    private static HttpResponseMessage JsonResponse(string value) => new(HttpStatusCode.OK)
    {
        Content = JsonContent(value)
    };

    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request, body);
        }
    }
}
