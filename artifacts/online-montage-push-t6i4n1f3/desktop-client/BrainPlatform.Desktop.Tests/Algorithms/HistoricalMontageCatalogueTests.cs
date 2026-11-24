using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class HistoricalMontageCatalogueTests
{
    [Fact]
    public void DeduplicateKeepsOneProjectionAndPreservesDifferentOutputs()
    {
        var duplicate = Descriptor("legacy-id", "同一导联", "F3-REF");
        var current = Descriptor("current-id", "同一导联", "F3-REF");
        var different = Descriptor("different-id", "同一导联", "F3-Fz");

        var result = HistoricalMontageCatalogue.Deduplicate([current, duplicate, different]);

        Assert.Equal(["current-id", "different-id"], result.Select(profile => profile.Id));
    }

    private static HistoricalMontageDescriptor Descriptor(string id, string label, string outputId) =>
        new(id, "1", $"fingerprint-{id}", label, id == "current-id", true, null,
            [new HistoricalMontageOutput(outputId, outputId, "F3", "V", true, null)]);
}
