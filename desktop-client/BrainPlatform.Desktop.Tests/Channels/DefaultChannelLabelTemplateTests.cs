using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class DefaultChannelLabelTemplateTests
{
    [Fact]
    public void Template_AppliesOnlyToTheExpectedPhysicalInputLayout()
    {
        var capabilities = Enumerable.Range(0, 24)
            .Select(index => new AcquisitionChannelCapability(index, AcquisitionChannelKind.Reference, "V"))
            .Concat(Enumerable.Range(24, 4)
                .Select(index => new AcquisitionChannelCapability(index, AcquisitionChannelKind.Bipolar, "V")))
            .ToArray();

        Assert.True(DefaultChannelLabelTemplate.TryGetFor(capabilities, out var labels));
        Assert.Equal("F3", labels[3]);
        Assert.Equal("O2", labels[21]);
        Assert.DoesNotContain(20, labels.Keys);
        Assert.DoesNotContain(24, labels.Keys);
    }

    [Fact]
    public void Template_DoesNotApplyToAnUnknownDeviceLayout()
    {
        Assert.False(DefaultChannelLabelTemplate.TryGetFor(
            [new AcquisitionChannelCapability(0, AcquisitionChannelKind.Reference, "V")],
            out var labels));
        Assert.Empty(labels);
    }
}
