using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class AcquisitionDeviceCompatibilityTests
{
    [Fact]
    public void Signature_IgnoresDeviceInstanceButIncludesDriverAndFullPhysicalCapabilities()
    {
        var capabilities = new[]
        {
            new AcquisitionChannelCapability(0, AcquisitionChannelKind.Reference, "V"),
            new AcquisitionChannelCapability(24, AcquisitionChannelKind.Bipolar, "V"),
            new AcquisitionChannelCapability(28, AcquisitionChannelKind.SampleCounter, "count"),
        };

        var first = AcquisitionDeviceCompatibility.CreateSignature("ant-eego", capabilities);
        var sameModelDifferentInstance = AcquisitionDeviceCompatibility.CreateSignature("ant-eego", capabilities);
        var differentDriver = AcquisitionDeviceCompatibility.CreateSignature("other-driver", capabilities);
        var differentUnit = AcquisitionDeviceCompatibility.CreateSignature("ant-eego", [
            new AcquisitionChannelCapability(0, AcquisitionChannelKind.Reference, "mV"),
            new AcquisitionChannelCapability(24, AcquisitionChannelKind.Bipolar, "V"),
            new AcquisitionChannelCapability(28, AcquisitionChannelKind.SampleCounter, "count"),
        ]);

        Assert.Equal(first, sameModelDifferentInstance);
        Assert.NotEqual(first, differentDriver);
        Assert.NotEqual(first, differentUnit);
    }
}
