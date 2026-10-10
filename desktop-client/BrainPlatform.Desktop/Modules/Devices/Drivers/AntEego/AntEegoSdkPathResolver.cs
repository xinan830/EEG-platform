using System.IO;

namespace BrainPlatform.Desktop.Modules.Devices.Drivers.AntEego;

public static class AntEegoSdkPathResolver
{
    public static string Resolve(string configuredPath, string applicationDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var bundledPath = Path.Combine(applicationDirectory, "sdk", "eego-SDK.dll");
        return File.Exists(bundledPath) ? bundledPath : configuredPath;
    }
}
