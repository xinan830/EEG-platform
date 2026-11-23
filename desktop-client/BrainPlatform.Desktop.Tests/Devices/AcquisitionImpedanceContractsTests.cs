using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Devices.Contracts;

namespace BrainPlatform.Desktop.Tests.Devices;

public sealed class AcquisitionImpedanceContractsTests
{
    [Fact]
    public void Snapshot_PreservesSdkOrderAndDoesNotRequireAnEegSampleCounter()
    {
        var sessionId = Guid.NewGuid();
        var metadata = new ImpedanceStreamMetadata("ant-eego:1", "ANT", [
            new ImpedanceChannel(0, 22, AcquisitionChannelKind.ImpedanceReference, "Pz"),
            new ImpedanceChannel(1, 18, AcquisitionChannelKind.ImpedanceGround, "GND"),
        ]);

        var snapshot = ImpedanceSnapshot.Create(sessionId, 1, DateTimeOffset.UtcNow, metadata, [2500d, 5000d]);

        Assert.Equal(sessionId, snapshot.DetectionSessionId);
        Assert.Equal([22, 18], snapshot.Readings.Select(reading => reading.NativeChannelIndex));
        Assert.Equal([2500d, 5000d], snapshot.Readings.Select(reading => reading.ResistanceOhms));
        Assert.All(snapshot.Readings, reading => Assert.Equal(ImpedanceReadingStatus.Valid, reading.Status));
    }

    [Fact]
    public void Snapshot_RepeatedValuesRemainValidAndUseLocalReceiptSequence()
    {
        var metadata = CreateMetadata();
        var first = ImpedanceSnapshot.Create(Guid.NewGuid(), 1, DateTimeOffset.UtcNow, metadata, [1200d]);
        var second = ImpedanceSnapshot.Create(first.DetectionSessionId, 2, DateTimeOffset.UtcNow, metadata, [1200d]);

        Assert.Equal(first.Readings[0].ResistanceOhms, second.Readings[0].ResistanceOhms);
        Assert.Equal(2, second.UpdateSequence);
        Assert.Equal(ImpedanceReadingStatus.Valid, second.Readings[0].Status);
    }

    [Fact]
    public void Request_PreservesPhysicalChannelRoleAndNativeIdentity()
    {
        var channels = new[]
        {
            new ImpedanceChannelIdentity(22, AcquisitionChannelKind.Reference),
            new ImpedanceChannelIdentity(18, AcquisitionChannelKind.Reference),
        };
        var request = new ImpedanceStreamRequest(Guid.NewGuid(), "ant-eego:1", channels);

        request.Validate();

        Assert.Equal(channels, request.Channels);
    }

    [Fact]
    public void AdapterCapability_RemainsUnknownUntilTheDeviceIsProbed()
    {
        Assert.Equal(ImpedanceCapabilityStatus.Unknown, new UnknownImpedanceCapabilityAdapter().ImpedanceCapability);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    public void Snapshot_MarksInvalidSdkValuesWithoutReturningThemAsResistance(double value)
    {
        var snapshot = ImpedanceSnapshot.Create(Guid.NewGuid(), 1, DateTimeOffset.UtcNow, CreateMetadata(), [value]);

        Assert.Equal(ImpedanceReadingStatus.Invalid, snapshot.Readings[0].Status);
        Assert.Null(snapshot.Readings[0].ResistanceOhms);
    }

    [Fact]
    public void LegacyEegAdapterDoesNotClaimOptionalImpedanceCapability()
    {
        Assert.IsAssignableFrom<IAcquisitionDeviceAdapter>(new UnavailableAcquisitionDeviceAdapter());
        Assert.False((object)new UnavailableAcquisitionDeviceAdapter() is IAcquisitionImpedanceDeviceAdapter);
    }

    private static ImpedanceStreamMetadata CreateMetadata() => new("ant-eego:1", "ANT", [
        new ImpedanceChannel(0, 10, AcquisitionChannelKind.ImpedanceReference, "Fz"),
    ]);

    private sealed class UnknownImpedanceCapabilityAdapter : IAcquisitionImpedanceDeviceAdapter
    {
        public ImpedanceCapabilityStatus ImpedanceCapability => ImpedanceCapabilityStatus.Unknown;

        public Task<IAcquisitionImpedanceStream> OpenImpedanceStreamAsync(
            ImpedanceStreamRequest request,
            CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
