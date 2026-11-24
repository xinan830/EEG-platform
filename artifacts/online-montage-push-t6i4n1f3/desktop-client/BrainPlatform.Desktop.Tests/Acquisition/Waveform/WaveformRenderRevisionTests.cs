namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class WaveformRenderRevisionTests
{
    [Fact]
    public void CompleteProfileAndVisibilityArePartOfRenderIdentity()
    {
        var montage = TemporaryMontageFixture.Create("F3", "F4");
        var source = new LiveWaveformSource(new AcquisitionStreamMetadata("device", "device", 2,
            [new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
             new AcquisitionChannel(1, 1, "F4", AcquisitionChannelKind.Reference, "V")], 2, DateTimeOffset.UnixEpoch),
            [new AcquisitionBatch(0, 2, 2, [1d, 2d, 3d, 4d], DateTimeOffset.UnixEpoch)],
            [new LiveDisplayChannel(0, 0, "F3", "Reference", true), new LiveDisplayChannel(1, 1, "F4", "Reference", true)],
            MontageProfile: montage);
        WaveformRenderRevision Identity(LiveWaveformSource value) => WaveformRenderRevision.Create(value, 10, 1000, 10, 600);
        var original = Identity(source);
        var recolored = montage with
        {
            DerivedChannels = montage.DerivedChannels.Select(channel => channel with { Color = "#ABCDEF" }).ToArray(),
        };
        Assert.NotEqual(original, Identity(source with { MontageProfile = recolored }));
        Assert.NotEqual(original, Identity(source with { MontageProfile = null }));
        Assert.NotEqual(original, Identity(source with { VisibleMontageChannelNames = new HashSet<string> { "F3-F4" } }));
        Assert.Equal(original, Identity(source with { MontageProfile = montage with { Name = "只改配置名称" } }));
    }
}
