using BrainPlatform.Desktop.Modules.Channels.Api;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.Services;

namespace BrainPlatform.Desktop.Tests.Channels;

public sealed class AntSystemChannelLocationResolverTests
{
    [Fact]
    public async Task ResolveAsync_AttachesLocationsWithoutChangingChannelConfiguration()
    {
        var profile = CreateProfile();
        var client = new StubLocationClient(labels =>
        [
            Available(labels[0], "Fp1", -0.03, 0.08, 0),
            Available(labels[1], "F3", -0.04, 0.04, 0),
        ]);

        var resolved = await AntSystemChannelLocationResolver.ResolveAsync(profile, client, CancellationToken.None);

        Assert.Equal(profile.Channels.Select(channel => channel.NativeChannelIndex),
            resolved.Channels.Select(channel => channel.NativeChannelIndex));
        Assert.Equal(profile.Channels.Select(channel => channel.ElectrodeLabel),
            resolved.Channels.Select(channel => channel.ElectrodeLabel));
        Assert.Equal(profile.Channels.Select(channel => channel.IsSelectedForDisplay),
            resolved.Channels.Select(channel => channel.IsSelectedForDisplay));
        Assert.Equal("Fp1", resolved.Channels[0].ElectrodeLocation?.CanonicalSite);
        Assert.Null(resolved.Channels[1].ElectrodeLocation);
        Assert.Equal("F3", resolved.Channels[2].ElectrodeLocation?.CanonicalSite);
        Assert.Equal("REF", resolved.HardwareReferenceElectrodeLocation);
        Assert.Equal("GND", resolved.HardwareGroundElectrodeLocation);
        Assert.Equal(new[] { "Fp1", "F3" }, client.RequestedLabels);
    }

    [Fact]
    public async Task ResolveAsync_RejectsOutOfOrderResponseInsteadOfMislabelingPositions()
    {
        var profile = CreateProfile();
        var client = new StubLocationClient(labels =>
        [
            Available(labels[1], "F3", 0, 0, 0),
            Unavailable(labels[0]),
        ]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AntSystemChannelLocationResolver.ResolveAsync(profile, client, CancellationToken.None));

        Assert.Contains("顺序", error.Message);
    }

    [Fact]
    public async Task TryResolveAsync_KeepsProfileUsableWhenLocationServiceFails()
    {
        var profile = CreateProfile();
        var client = new StubLocationClient(_ => throw new HttpRequestException("backend offline"));

        var result = await AntSystemChannelLocationResolver.TryResolveAsync(profile, client, CancellationToken.None);

        Assert.Same(profile, result.Profile);
        Assert.Contains("仍可使用", result.Warning);
        Assert.Contains("backend offline", result.Warning);
    }

    private static ChannelConfigurationProfile CreateProfile() => new(
        "system-ant-standard-28-input", "ANT默认通道配置", "test", ChannelConfigurationSource.System,
        "0:Reference;1:Reference;2:Bipolar",
        [
            new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "Fp1", true, 0),
            new ChannelConfigurationEntry(1, AcquisitionChannelKind.Reference, "REF", false, 1),
            new ChannelConfigurationEntry(2, AcquisitionChannelKind.Bipolar, "F3", true, 2),
        ],
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static ElectrodeLocationResolution Available(
        string label, string canonical, double x, double y, double z) =>
        new(label, "available", canonical, [x, y, z], "mri", "m", "mne.standard_1020", "1.12.1");

    private static ElectrodeLocationResolution Unavailable(string label) =>
        new(label, "unavailable", null, null, null, null, "mne.standard_1020", "1.12.1");

    private sealed class StubLocationClient(
        Func<IReadOnlyList<string>, IReadOnlyList<ElectrodeLocationResolution>> resolve) : IElectrodeLocationClient
    {
        public IReadOnlyList<string> RequestedLabels { get; private set; } = [];

        public Task<IReadOnlyList<ElectrodeLocationResolution>> ResolveAsync(
            IReadOnlyList<string> labels,
            CancellationToken cancellationToken)
        {
            RequestedLabels = labels.ToArray();
            return Task.FromResult(resolve(labels));
        }
    }
}
