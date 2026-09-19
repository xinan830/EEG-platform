using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Tests;

public sealed class UnavailableDeviceAdapterTests
{
    [Fact]
    public void GetReadiness_DoesNotPretendADeviceExists()
    {
        var readiness = new UnavailableAcquisitionDeviceAdapter().GetReadiness();

        Assert.False(readiness.IsAvailable);
        Assert.Equal("未配置", readiness.StatusText);
    }
}
