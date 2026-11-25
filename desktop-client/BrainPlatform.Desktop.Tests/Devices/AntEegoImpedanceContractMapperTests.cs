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
    public void CreateMetadata_MapsCompactSdkIndexesToRequestedPhysicalChannelsInStreamOrder()
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
        Assert.Equal([1, 0], metadata.Channels.Select(channel => channel.NativeChannelIndex));
        Assert.Equal(["Cz", "Fz"], metadata.Channels.Select(channel => channel.Label));
    }

    [Fact]
    public void CreateMetadata_MapsRealConfiguredInputsWhenSdkRenumbersThemFromZero()
    {
        var requested = new[]
        {
            new ImpedanceChannelIdentity(0, AcquisitionChannelKind.Reference),
            new ImpedanceChannelIdentity(1, AcquisitionChannelKind.Reference),
            new ImpedanceChannelIdentity(2, AcquisitionChannelKind.Reference),
            new ImpedanceChannelIdentity(3, AcquisitionChannelKind.Reference),
            new ImpedanceChannelIdentity(19, AcquisitionChannelKind.Reference),
            new ImpedanceChannelIdentity(21, AcquisitionChannelKind.Reference),
        };
        var sdkChannels = Enumerable.Range(0, 6)
            .Select(index => new AntEegoNativeChannelInfo { Index = index, Type = AntEegoNativeChannelType.Reference })
            .Concat([
                new() { Index = 6, Type = AntEegoNativeChannelType.ImpedanceReference },
                new() { Index = 7, Type = AntEegoNativeChannelType.ImpedanceGround },
            ]).ToArray();

        var metadata = AntEegoImpedanceContractMapper.CreateMetadata("ant-eego:1", "ANT", requested, sdkChannels,
            new Dictionary<int, string> { [0] = "Fp1", [1] = "Fp2", [2] = "F7", [3] = "F3", [19] = "O1", [21] = "O2" });

        Assert.Equal(requested, metadata.Channels.Where(channel => channel.Kind == AcquisitionChannelKind.Reference)
            .Select(channel => new ImpedanceChannelIdentity(channel.NativeChannelIndex, channel.Kind)));
        Assert.Equal(["Fp1", "Fp2", "F7", "F3", "O1", "O2"], metadata.Channels
            .Where(channel => channel.Kind == AcquisitionChannelKind.Reference).Select(channel => channel.Label));
        Assert.Empty(metadata.UnavailableRequestedChannels ?? []);
    }

    [Fact]
    public void CreateMetadata_RejectsSdkIdentityThatDoesNotMatchPhysicalRequest()
    {
        var error = Assert.Throws<AntEegoSdkException>(() => AntEegoImpedanceContractMapper.CreateMetadata(
            "ant-eego:1",
            "ANT",
            [new(2, AcquisitionChannelKind.Reference)],
            [new() { Index = 2, Type = AntEegoNativeChannelType.ImpedanceGround }],
            null));

        Assert.Contains("requested=[Reference:2]", error.Message);
        Assert.Contains("SDK=[ImpedanceGround:2]", error.Message);
        Assert.Contains("sdk_physical_count=0", error.Message);
    }

    [Fact]
    public void CreateMetadata_RejectsUnrequestedPhysicalInputBecauseItsIdentityCannotBeProven()
    {
        Assert.Throws<AntEegoSdkException>(() => AntEegoImpedanceContractMapper.CreateMetadata(
            "ant-eego:1", "ANT", [new(2, AcquisitionChannelKind.Reference)],
            [
                new() { Index = 0, Type = AntEegoNativeChannelType.Reference },
                new() { Index = 2, Type = AntEegoNativeChannelType.Reference },
                new() { Index = 24, Type = AntEegoNativeChannelType.ImpedanceReference },
                new() { Index = 25, Type = AntEegoNativeChannelType.ImpedanceGround },
            ],
            new Dictionary<int, string> { [0] = "Fz", [2] = "Cz" }));
    }

    [Fact]
    public void CreateMetadata_RejectsIncompletePhysicalInputTableInsteadOfGuessingElectrodeIdentity()
    {
        Assert.Throws<AntEegoSdkException>(() => AntEegoImpedanceContractMapper.CreateMetadata(
            "ant-eego:1", "ANT", [new(19, AcquisitionChannelKind.Reference), new(21, AcquisitionChannelKind.Reference)],
            [
                new() { Index = 19, Type = AntEegoNativeChannelType.Reference },
                new() { Index = 21, Type = AntEegoNativeChannelType.ImpedanceReference },
                new() { Index = 22, Type = AntEegoNativeChannelType.ImpedanceGround },
            ],
            new Dictionary<int, string> { [19] = "O1", [21] = "O2" }));
    }

    [Fact]
    public void CreateMetadata_RejectsAStreamWithNoRequestedPhysicalInput()
    {
        Assert.Throws<AntEegoSdkException>(() => AntEegoImpedanceContractMapper.CreateMetadata(
            "ant-eego:1", "ANT", [new(21, AcquisitionChannelKind.Reference)],
            [
                new() { Index = 20, Type = AntEegoNativeChannelType.ImpedanceReference },
                new() { Index = 21, Type = AntEegoNativeChannelType.ImpedanceGround },
            ], null));
    }

    [Fact]
    public void CreateMetadata_AllowsSameSdkIndexForDifferentImpedanceRoles()
    {
        var metadata = AntEegoImpedanceContractMapper.CreateMetadata(
            "ant-eego:1", "ANT", [new(2, AcquisitionChannelKind.Reference)],
            [
                new() { Index = 0, Type = AntEegoNativeChannelType.Reference },
                new() { Index = 0, Type = AntEegoNativeChannelType.ImpedanceReference },
                new() { Index = 0, Type = AntEegoNativeChannelType.ImpedanceGround },
            ], null);

        Assert.Equal([2, 0, 0], metadata.Channels.Select(channel => channel.NativeChannelIndex));
    }
}
