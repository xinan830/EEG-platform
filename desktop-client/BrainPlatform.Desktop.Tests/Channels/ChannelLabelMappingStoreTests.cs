
namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class ChannelLabelMappingStoreTests
{
    [Fact]
    public async Task Store_RoundTripsOnlyTheSavedPhysicalChannelMapping()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "channel-label-mappings.json");
        var store = new ChannelLabelMappingStore(path);

        await store.SaveAsync("serial-200586", new Dictionary<int, string> { [0] = "Fp1", [7] = "F3" }, CancellationToken.None);

        Assert.Equal("Fp1", store.Load("serial-200586")[0]);
        Assert.Equal("F3", store.Load("serial-200586")[7]);
        Assert.Empty(store.Load("other-device"));
    }

    [Fact]
    public async Task Store_DistinguishesAnExplicitEmptyMappingFromNoSavedMapping()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "channel-label-mappings.json");
        var store = new ChannelLabelMappingStore(path);
        await store.SaveAsync("serial", new Dictionary<int, string>(), CancellationToken.None);

        Assert.True(store.TryLoad("serial", out var saved));
        Assert.Empty(saved);
        Assert.False(store.TryLoad("other-device", out _));
    }
}
