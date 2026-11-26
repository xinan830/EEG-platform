using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Channels.Api;
using BrainPlatform.Desktop.Shared.Controls.Topomap;

namespace BrainPlatform.Desktop.Tests.Channels;

public sealed class TemplateElectrodeAtlasTests
{
    [Fact]
    public async Task HttpElectrodeLocationClient_ReadsMontageCatalogAndPreview()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/topomap/3d/montages")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        default_montage = "colin27_1020",
                        montages = new[]
                        {
                            new
                            {
                                name = "colin27_1020",
                                display_name = "标准 10-20 · Colin27（系统默认）",
                                category = "国际标准系列",
                                channel_count = 94,
                                is_default = true,
                            },
                            new
                            {
                                name = "biosemi128",
                                display_name = "BioSemi 128",
                                category = "BioSemi 系列",
                                channel_count = 128,
                                is_default = false,
                            },
                        },
                    }),
                };
            }

            Assert.Equal("/api/topomap/3d/montages/biosemi128", request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    montage_name = "biosemi128",
                    display_name = "BioSemi 128",
                    is_default = false,
                    channel_count = 1,
                    projection_source = "dynamic_head_mesh_projection",
                    sites = new[]
                    {
                        new
                        {
                            channel = "A1",
                            canonical_channel = "A1",
                            map_x = 0.0,
                            map_y = 0.0,
                            coordinate_gnm = new[] { 0.0, 0.31, 0.14 },
                            is_scalp_field_site = true,
                        },
                    },
                }),
            };
        })) { BaseAddress = new Uri("http://localhost/") };

        var api = new HttpElectrodeLocationClient(client);
        var catalog = await api.GetMontageCatalogAsync(CancellationToken.None);
        var preview = await api.GetMontagePreviewAsync("biosemi128", CancellationToken.None);

        Assert.Equal("colin27_1020", catalog.DefaultMontage);
        Assert.Equal(2, catalog.Montages.Count);
        Assert.True(catalog.Montages[0].IsDefault);
        Assert.Equal("dynamic_head_mesh_projection", preview.ProjectionSource);
        Assert.Equal("A1", preview.Sites.Single().Channel);
        Assert.Equal([0.0, 0.31, 0.14], preview.Sites.Single().CoordinateGnm);
    }

    [Fact]
    public void Topomap3DWebPayloadBuilder_UsesCustomLocationsWithoutStandardNameFiltering()
    {
        var scalp = new ElectrodeScalpModel3D(
            "gnm-colin27-head", "gnm-skin-mesh-v1", "mne.colin27_1020-to-gnm-head", 4,
            "m", "a", "g", "colin27_1020", "1.13.2", "head", "m", "gnm_native", "GNM model units",
            "standard_template_visualization", "ok",
            [0, 0.28, 0.04], [-1, 0, 0], [0, 0.29, 0.96], [0, 0.96, -0.29],
            [new ElectrodeLocation3D("Cz", "Cz", [0, 0.31, 0.14], "anchor", false, true)],
            [[0, 0, 0]], [[0, 0, 0]]);

        var custom = Enumerable.Range(1, 128)
            .Select(i => new ElectrodeLocation3D($"BS_{i}", $"BS_{i}", [0.01 * i, 0.30, 0.10], "dynamic", false, true))
            .ToArray();

        var json = Topomap3DWebPayloadBuilder.BuildSceneStateJson(
            scalp,
            Array.Empty<string>(),
            field: null,
            scaleState: null,
            isPreview: false,
            showDim: false,
            windowText: null,
            enableLabModes: false,
            hideInternalModeTabs: true,
            forcedMode: "electrodes",
            customLocations: custom,
            showLabels: false);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("showLabels").GetBoolean());
        var electrodes = doc.RootElement.GetProperty("electrodes");
        Assert.Equal(128, electrodes.GetArrayLength());
        Assert.Equal("BS_1", electrodes[0].GetProperty("label").GetString());
        Assert.Equal("BS_128", electrodes[127].GetProperty("label").GetString());
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
