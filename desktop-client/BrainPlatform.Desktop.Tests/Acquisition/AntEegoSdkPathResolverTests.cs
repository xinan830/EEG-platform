namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class AntEegoSdkPathResolverTests
{
    [Theory]
    [InlineData("")]
    [InlineData("missing-sdk.dll")]
    public void MissingLocalPathUsesBundle(string configuredPath)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sdk = Path.Combine(root, "sdk", "eego-SDK.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(sdk)!);
        try
        {
            File.WriteAllBytes(sdk, []);
            Assert.Equal(sdk, AntEegoSdkPathResolver.Resolve(configuredPath, root));
            Assert.Equal(sdk, AntEegoSdkPathResolver.Resolve(sdk, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidLocalPathRemainsPreferred()
    {
        var local = Path.GetTempFileName();
        try
        {
            Assert.Equal(local, AntEegoSdkPathResolver.Resolve(local, AppContext.BaseDirectory));
        }
        finally
        {
            File.Delete(local);
        }
    }

    [Fact]
    public void MissingBundlePreservesConfigurationForDiagnostics()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Equal("missing-sdk.dll", AntEegoSdkPathResolver.Resolve("missing-sdk.dll", missingDirectory));
        Assert.Equal("", AntEegoSdkPathResolver.Resolve("", missingDirectory));
    }
}
