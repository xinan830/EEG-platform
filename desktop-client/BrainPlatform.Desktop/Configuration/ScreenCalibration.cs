using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

namespace BrainPlatform.Desktop.Configuration;

/// <summary>Physical display calibration used by paper-speed rendering.</summary>
public sealed record ScreenCalibrationProfile(
    string DisplayKey,
    double WidthCentimeters,
    double HeightCentimeters,
    DateTimeOffset UpdatedAtUtc)
{
    public bool IsValid => WidthCentimeters > 0 && HeightCentimeters > 0;
}

public readonly record struct PrimaryScreenMetrics(int WidthPixels, int HeightPixels, uint Dpi)
{
    public double ScalePercent => Dpi / 96d * 100d;
    public double WidthDips => WidthPixels * 96d / Dpi;
    public double HeightDips => HeightPixels * 96d / Dpi;
}

public readonly record struct ScreenDisplayMetrics(
    string DisplayKey,
    int WidthPixels,
    int HeightPixels,
    double DpiScaleX,
    double DpiScaleY)
{
    public double WidthDips => WidthPixels / DpiScaleX;
    public double HeightDips => HeightPixels / DpiScaleY;
    public double DpiX => DpiScaleX * 96d;
    public double DpiY => DpiScaleY * 96d;
    public double ScalePercent => DpiScaleX * 100d;
}

public sealed class ScreenCalibrationStore
{
    private readonly string path;

    public ScreenCalibrationStore(string? path = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrainPlatform",
            "screen-calibration.json");
    }

    public ScreenCalibrationProfile? Load(string displayKey)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var profiles = JsonSerializer.Deserialize<List<ScreenCalibrationProfile>>(File.ReadAllText(path)) ?? [];
            return profiles.FirstOrDefault(profile =>
                string.Equals(profile.DisplayKey, displayKey, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(ScreenCalibrationProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Screen calibration path has no directory."));
        var profiles = File.Exists(path)
            ? JsonSerializer.Deserialize<List<ScreenCalibrationProfile>>(File.ReadAllText(path)) ?? []
            : [];
        profiles.RemoveAll(item => string.Equals(item.DisplayKey, profile.DisplayKey, StringComparison.OrdinalIgnoreCase));
        profiles.Add(profile);
        File.WriteAllText(path, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public readonly record struct ScreenScaleContext(
    string DisplayKey,
    double MillimetersPerDipX,
    double MillimetersPerDipY)
{
    public static ScreenScaleContext Nominal { get; } = new(
        "nominal",
        25.4d / 96d,
        25.4d / 96d);

    public double DipsPerMillimeterX => 1d / MillimetersPerDipX;
    public double DipsPerMillimeterY => 1d / MillimetersPerDipY;
}

/// <summary>Resolves physical screen conversion for one owning WPF window.</summary>
public static class ScreenCalibrationMetrics
{
    public static event EventHandler? CalibrationChanged;

    public static double GetPrimaryScreenWidthDips()
    {
        return GetPrimaryScreenMetrics().WidthDips;
    }

    public static ScreenDisplayMetrics GetDisplayMetrics(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        var nativeDpi = handle == IntPtr.Zero ? 0u : GetDpiForWindow(handle);
        var systemDpi = GetDpiForSystem();
        // A DPI-unaware process can receive the virtualized 96 DPI value even
        // for a high-scale monitor. Prefer the system scale in that case.
        if (nativeDpi <= 96 && systemDpi > 96)
        {
            nativeDpi = systemDpi;
        }
        if (nativeDpi == 0)
        {
            nativeDpi = systemDpi;
        }
        if (nativeDpi == 0)
        {
            nativeDpi = 96;
        }
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            var width = Math.Abs(info.rcMonitor.right - info.rcMonitor.left);
            var height = Math.Abs(info.rcMonitor.bottom - info.rcMonitor.top);
            return new ScreenDisplayMetrics(
                info.szDevice,
                width,
                height,
                nativeDpi / 96d,
                nativeDpi / 96d);
        }

        var fallback = GetPrimaryScreenMetrics();
        return new ScreenDisplayMetrics(
            "primary-display",
            fallback.WidthPixels,
            fallback.HeightPixels,
            fallback.Dpi / 96d,
            fallback.Dpi / 96d);
    }

    public static PrimaryScreenMetrics GetPrimaryScreenMetrics()
    {
        var dpi = GetDpiForSystem();
        if (dpi == 0)
        {
            dpi = 96;
        }

        var width = GetSystemMetricsForDpi(0, dpi);
        var height = GetSystemMetricsForDpi(1, dpi);
        return new PrimaryScreenMetrics(width, height, dpi);
    }

    public static ScreenScaleContext ResolveScale(
        Window window,
        ScreenCalibrationStore? store = null)
    {
        var metrics = GetDisplayMetrics(window);
        var profile = (store ?? new ScreenCalibrationStore()).Load(metrics.DisplayKey);
        if (profile is null || !profile.IsValid || metrics.WidthDips <= 0 || metrics.HeightDips <= 0)
        {
            return ScreenScaleContext.Nominal with { DisplayKey = metrics.DisplayKey };
        }

        return new ScreenScaleContext(
            metrics.DisplayKey,
            profile.WidthCentimeters * 10d / metrics.WidthDips,
            profile.HeightCentimeters * 10d / metrics.HeightDips);
    }

    public static void NotifyCalibrationChanged() =>
        CalibrationChanged?.Invoke(null, EventArgs.Empty);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorRect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public MonitorRect rcMonitor;
        public MonitorRect rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
