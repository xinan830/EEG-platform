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

    private static AlgorithmCatalogItem Catalog(string capabilities) => new(
        "official", "test", "current-v1", "测试", "T", "", [], [], default,
        new DynamicAnalysisPolicy(4, [5], 5, 1, true), "available", true,
        null, null, null, JsonDocument.Parse(capabilities).RootElement);
}
