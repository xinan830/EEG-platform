
namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class AntEegoContractsTests
{
    [Fact]
    public void Mapper_PreservesNativeRolesUnitsAndNoGuessedElectrodeLabel()
    {
        var eeg = AntEegoChannelMapper.Map(0, 17, AntEegoNativeChannelType.Reference);
        var counter = AntEegoChannelMapper.Map(1, 31, AntEegoNativeChannelType.SampleCounter);
        var trigger = AntEegoChannelMapper.Map(2, 32, AntEegoNativeChannelType.Trigger);
        var accelerometer = AntEegoChannelMapper.Map(3, 33, AntEegoNativeChannelType.Accelerometer);

        Assert.Equal((AcquisitionChannelKind.Reference, "V", null), (eeg.Kind, eeg.Unit, eeg.Label));
        Assert.Equal((AcquisitionChannelKind.SampleCounter, "count", null), (counter.Kind, counter.Unit, counter.Label));
        Assert.Equal((AcquisitionChannelKind.Trigger, "code", null), (trigger.Kind, trigger.Unit, trigger.Label));
        Assert.Equal((AcquisitionChannelKind.Accelerometer, "vendor_native", null), (accelerometer.Kind, accelerometer.Unit, accelerometer.Label));
    }

    [Fact]
    public void Mapper_UsesOnlyExplicitUserConfirmedNativeChannelLabel()
    {
        var channel = AntEegoChannelMapper.Map(
            0,
            17,
            AntEegoNativeChannelType.Reference,
            new Dictionary<int, string> { [17] = "F3" });

        Assert.Equal("F3", channel.Label);
    }

    [Fact]
    public void CounterReader_AcceptsConsecutiveSampleMajorValues()
    {
        var first = AntEegoSampleCounterReader.ReadAndValidateFirstCounter(
            [1e-6, 100, 2e-6, 101, 3e-6, 102], 3, 2, 1);

        Assert.Equal(100, first);
    }

    [Theory]
    [InlineData(double.NaN, 101d)]
    [InlineData(100.5d, 101.5d)]
    [InlineData(100d, 102d)]
    public void CounterReader_RejectsInvalidOrNonconsecutiveValues(double first, double second)
    {
        Assert.Throws<AcquisitionContinuityException>(() =>
            AntEegoSampleCounterReader.ReadAndValidateFirstCounter([1e-6, first, 2e-6, second], 2, 2, 1));
    }

    [Theory]
    [InlineData("ant-eego:-1")]
    [InlineData("ant-eego:abc")]
    [InlineData("other:3")]
    public void DeviceIdentity_RejectsInvalidIdentifiers(string deviceId)
    {
        Assert.Throws<ArgumentException>(() => AntEegoDeviceIdentity.Parse(deviceId));
    }

    [Fact]
    public void StreamOptions_RequireExplicitPositiveRanges()
    {
        var options = new AntEegoAdapterOptions("C:\\sdk\\eego-SDK.dll", null, 0);

        Assert.Throws<ArgumentException>(options.ValidateForStream);
    }

    [Theory]
    [InlineData(0.001d, 0.0025d, true)]
    [InlineData(0.001d, 0.002d, false)]
    [InlineData(0.002d, 0.005d, true)]
    public void RangePair_RequiresTheSdkAuxiliaryRatio(double referenceRange, double bipolarRange, bool expected)
    {
        Assert.Equal(expected, AntEegoRangePair.IsCompatible(referenceRange, bipolarRange));
    }

    [Fact]
    public void RangePair_SelectsTheFirstDeviceSupportedCompatiblePair()
    {
        var pair = AntEegoRangePair.FindFirstCompatiblePair(
            [0.5d, 1d],
            [1d, 1.25d, 2.5d]);

        Assert.Equal((0.5d, 1.25d), pair);
    }

    [Fact]
    public void StreamOptions_RejectAnIncompatibleRangePairBeforeOpeningTheDevice()
    {
        var options = new AntEegoAdapterOptions("C:\\sdk\\eego-SDK.dll", 0.001d, 0.002d);

        var exception = Assert.Throws<ArgumentException>(options.ValidateForStream);

        Assert.Contains("2.5", exception.Message);
    }

    [Fact]
    public void DriverConfiguration_PreservesOperatorRecordedHardwareLocations()
    {
        var configuration = AntEegoAcquisitionDriver.CreateConfiguration(new AntEegoAdapterOptions(
            "C:\\sdk\\eego-SDK.dll",
            0.001d,
            0.0025d,
            HardwareReferenceElectrodeLocation: " FCz ",
            HardwareGroundElectrodeLocation: "AFz"));

        Assert.Equal("FCz", configuration.GetOptionalSetting(AntEegoAcquisitionDriver.HardwareReferenceElectrodeLocationSetting));
        Assert.Equal("AFz", configuration.GetOptionalSetting(AntEegoAcquisitionDriver.HardwareGroundElectrodeLocationSetting));
    }

    [Fact]
    public void DriverConfiguration_PreservesChannelConfigurationSnapshot()
    {
        var configuration = AntEegoAcquisitionDriver.CreateConfiguration(new AntEegoAdapterOptions(
            "C:\\sdk\\eego-SDK.dll",
            0.001d,
            0.0025d,
            ChannelConfigurationId: "profile-123",
            ChannelConfigurationName: "静息态 28 通道",
            ChannelConfigurationSnapshotJson: "{\"id\":\"profile-123\"}"));

        Assert.Equal("profile-123", configuration.GetOptionalSetting(AntEegoAcquisitionDriver.ChannelConfigurationIdSetting));
        Assert.Equal("静息态 28 通道", configuration.GetOptionalSetting(AntEegoAcquisitionDriver.ChannelConfigurationNameSetting));
        Assert.Equal("{\"id\":\"profile-123\"}", configuration.GetOptionalSetting(AntEegoAcquisitionDriver.ChannelConfigurationSnapshotSetting));
    }

    [Fact]
    public async Task Discovery_WithMissingSdk_ReturnsStructuredErrorAndNoFakeDevice()
    {
        var adapter = new AntEegoAcquisitionAdapter(new AntEegoAdapterOptions(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "eego-SDK.dll"),
            0.001,
            0.001));

        var exception = await Assert.ThrowsAsync<AntEegoSdkException>(() => adapter.DiscoverAsync(CancellationToken.None));

        Assert.Equal("ANT_EEGO_SDK_NOT_FOUND", exception.Code);
    }
}
