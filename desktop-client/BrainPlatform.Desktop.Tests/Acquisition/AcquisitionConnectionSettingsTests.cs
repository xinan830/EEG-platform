using BrainPlatform.Desktop.Acquisition.AntEego;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Acquisition.Runtime;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class AcquisitionConnectionSettingsTests
{
    [Fact]
    public async Task LocalSettingsStore_RoundTripsOnlyConnectionPreferences()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new LocalAcquisitionSettingsStore(Path.Combine(directory, "acquisition-settings.json"));
            var expected = new AcquisitionConnectionSettings(
                "C:\\ANT\\eego-SDK.dll",
                "0.001",
                "0.002",
                "D:\\Recordings",
                1000);

            await store.SaveAsync(expected, CancellationToken.None);

            Assert.Equal(expected, store.Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Runtime_RequiresAppliedConfigurationBeforeDiscovery()
    {
        await using var runtime = new ConfiguredAcquisitionRuntime();

        await Assert.ThrowsAsync<AcquisitionUnavailableException>(() => runtime.DiscoverAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Runtime_AllowsRangeFreeConnectionTestButReportsMissingSdkWithoutDevices()
    {
        await using var runtime = new ConfiguredAcquisitionRuntime();
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "eego-SDK.dll");

        await runtime.ConfigureAsync(
            AntEegoAcquisitionDriver.CreateConfiguration(new AntEegoAdapterOptions(missingPath, null, null)),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AntEegoSdkException>(() => runtime.DiscoverAsync(CancellationToken.None));
        Assert.Equal("ANT_EEGO_SDK_NOT_FOUND", exception.Code);
        Assert.False(runtime.Readiness.IsAvailable);
    }

    [Fact]
    public void DeviceDescriptor_PreservesReturnedRangeCapabilities()
    {
        var device = new AcquisitionDeviceDescriptor(
            "ant-eego:3",
            "ANT/eego test",
            "serial",
            [500, 1000],
            [0.001, 0.002],
            [0.005]);

        Assert.Equal([0.001, 0.002], device.ReferenceRangesVolts);
        Assert.Equal([0.005], device.BipolarRangesVolts);
    }

    [Fact]
    public async Task LocalSettingsStore_RoundTripsPaperSpeedPreference()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new LocalAcquisitionSettingsStore(Path.Combine(directory, "acquisition-settings.json"));
            var settings = AcquisitionConnectionSettings.CreateDefault() with
            {
                PaperSpeedMillimetersPerSecond = 15,
            };

            await store.SaveAsync(settings, CancellationToken.None);

            Assert.Equal(15, store.Load().PaperSpeedMillimetersPerSecond);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LocalSettingsStore_RoundTripsHorizontalTimeScalePreference()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new LocalAcquisitionSettingsStore(Path.Combine(directory, "acquisition-settings.json"));
            var settings = AcquisitionConnectionSettings.CreateDefault() with
            {
                HorizontalTimeScaleMode = HorizontalTimeScaleMode.Timebase,
                TimebaseSecondsPerScreen = 15,
            };

            await store.SaveAsync(settings, CancellationToken.None);

            var loaded = store.Load();
            Assert.Equal(HorizontalTimeScaleMode.Timebase, loaded.HorizontalTimeScaleMode);
            Assert.Equal(15, loaded.TimebaseSecondsPerScreen);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
