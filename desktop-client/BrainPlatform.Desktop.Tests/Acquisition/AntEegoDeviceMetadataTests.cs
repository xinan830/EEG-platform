using BrainPlatform.Desktop.Acquisition.AntEego;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class AntEegoDeviceMetadataTests
{
    [Fact]
    public void Create_NormalizesEegoModelAndExtractsItsInstanceIdentifier()
    {
        var metadata = AntEegoDeviceMetadata.Create("EE511", "EE511-010010-200586");

        Assert.Equal("EE-511", metadata.Model);
        Assert.Equal("010010-200586", metadata.DeviceInstanceId);
    }

    [Fact]
    public void Create_PreservesAnUnrecognizedVendorIdentityWithoutGuessing()
    {
        var metadata = AntEegoDeviceMetadata.Create("eego-sport", "SPORT-001");

        Assert.Equal("eego-sport", metadata.Model);
        Assert.Equal("SPORT-001", metadata.DeviceInstanceId);
    }
}
