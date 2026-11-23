using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Devices.Contracts;
using BrainPlatform.Desktop.Modules.Devices.Drivers.AntEego;

namespace BrainPlatform.Desktop.Tests.Devices;

public sealed class AntEegoImpedanceContractMapperTests
{
    [Fact]
    public void ToNativeRequest_PreservesRequestedRoleAndPhysicalIndex()
    {
        var native = AntEegoImpedanceContractMapper.ToNativeRequest([
            new ImpedanceChannelIdentity(4, AcquisitionChannelKind.Reference),
            new ImpedanceChannelIdentity(2, AcquisitionChannelKind.Reference),
        ]);

        Assert.Equal(4, native[0].Index);
        Assert.Equal(AntEegoNativeChannelType.Reference, native[0].Type);
        Assert.Equal(2, native[1].Index);
        Assert.Equal(AntEegoNativeChannelType.Reference, native[1].Type);
    }

    [Fact]
    public void CreateMetadata_UsesActualSdkOrderEvenWhenItDiffersFromRequestOrder()
    {
        var metadata = AntEegoImpedanceContractMapper.CreateMetadata(
            "ant-eego:1",
            "ANT",
            [
                new(1, AcquisitionChannelKind.Reference),
                new(0, AcquisitionChannelKind.Reference),
            ],
            [
                new() { Index = 0, Type = AntEegoNativeChannelType.Reference },
                new() { Index = 1, Type = AntEegoNativeChannelType.Reference },
            ],
            new Dictionary<int, string> { [0] = "Fz", [1] = "Cz" });

        Assert.Equal([0, 1], metadata.Channels.Select(channel => channel.StreamIndex));
        Assert.Equal([0, 1], metadata.Channels.Select(channel => channel.NativeChannelIndex));
        Assert.Equal(["Fz", "Cz"], metadata.Channels.Select(channel => channel.Label));
    }

    [Fact]
    public void CreateMetadata_RejectsSdkIdentityThatDoesNotMatchPhysicalRequest()
    {
        Assert.Throws<AntEegoSdkException>(() => AntEegoImpedanceContractMapper.CreateMetadata(
            "ant-eego:1",
            "ANT",
            [new(2, AcquisitionChannelKind.Reference)],
            [new() { Index = 2, Type = AntEegoNativeChannelType.ImpedanceGround }],
            null));
    }
}
