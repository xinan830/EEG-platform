using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class AlgorithmCatalogCapabilityTests
{
    [Fact]
    public void Directional_reuse_requires_an_explicit_implemented_catalog_declaration()
    {
        var supported = Catalog("""{"full_result_to_preview":{"implementation":"implemented","validation":"validated"}}""");
        var unsupported = Catalog("""{"full_result_to_preview":{"implementation":"unsupported","validation":"not_applicable"}}""");
        var missing = Catalog("{}");
        var malformed = Catalog("""{"full_result_to_preview":{"implementation":true}}""");

        Assert.True(supported.SupportsDeclaredFullResultToPreview);
        Assert.False(unsupported.SupportsDeclaredFullResultToPreview);
        Assert.False(missing.SupportsDeclaredFullResultToPreview);
        Assert.False(malformed.SupportsDeclaredFullResultToPreview);
    }

    [Fact]
    public void Wpf_catalog_contract_reads_backend_identity_parameters_capabilities_and_outputs()
    {
        const string json = """
            {
              "source":"official","id":"psd","version":"offline-spectral-v3",
              "display_name_zh":"功率谱密度","abbreviation":"PSD","description":"PSD",
              "parameters":[{"key":"channel","label":"通道","type":"channel","required":true}],
              "modes":["static","dynamic"],"output_schema":{"fields":[{"name":"psd_uv2_hz","unit":"uV^2/Hz"}]},
              "dynamic_policy":{"minimum_window_s":4,"window_options_s":[5],"default_window_s":5,"refresh_step_s":1,"refresh_step_options_s":[1],"allow_warmup":true},
              "availability":"available","is_runnable":true,"definition_id":"definition-id",
              "definition_version":"3.0.0","implementation_identity":"psd-runtime-v3",
              "dynamic_capabilities":{"on_demand":{"implementation":"implemented","validation":"validated"}}
            }
            """;

        var item = JsonSerializer.Deserialize<AlgorithmCatalogItem>(json);

        Assert.NotNull(item);
        Assert.Equal("psd", item.Id);
        Assert.Equal("offline-spectral-v3", item.Version);
        Assert.Equal("psd-runtime-v3", item.ImplementationIdentity);
        Assert.Equal("channel", Assert.Single(item.Parameters).Key);
        Assert.Contains("dynamic", item.Modes);
        Assert.Equal("psd_uv2_hz", item.OutputSchema.GetProperty("fields")[0].GetProperty("name").GetString());
        Assert.True(item.SupportsDeclaredOnDemand);
    }

    private static AlgorithmCatalogItem Catalog(string capabilities) => new(
        "official", "test", "current-v1", "测试", "T", "", [], [], default,
        new DynamicAnalysisPolicy(4, [5], 5, 1, true), "available", true,
        null, null, null, JsonDocument.Parse(capabilities).RootElement);
}
