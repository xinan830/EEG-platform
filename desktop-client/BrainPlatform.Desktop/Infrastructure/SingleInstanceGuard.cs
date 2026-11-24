using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BrainPlatform.Desktop.Infrastructure;

/// <summary>
/// Enforces a single desktop client instance per Windows user session so a stray
/// duplicate process can never poll or contend for the live EEG amplifier USB link.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const int SwRestore = 9;
    private readonly Mutex mutex;
    private bool ownsMutex;

    private SingleInstanceGuard(Mutex mutex, bool ownsMutex)
    {
        this.mutex = mutex;
        this.ownsMutex = ownsMutex;
    }

    internal static SingleInstanceGuard? TryAcquire(string mutexName)
    {
        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (createdNew)
        {
            return new SingleInstanceGuard(mutex, ownsMutex: true);
        }

        // Recover cleanly if a previous process terminated abnormally while holding the mutex.
        try
        {
            if (mutex.WaitOne(0, exitContext: false))
            {
                return new SingleInstanceGuard(mutex, ownsMutex: true);
            }
        }
        catch (AbandonedMutexException)
        {
            return new SingleInstanceGuard(mutex, ownsMutex: true);
        }

        mutex.Dispose();
        return null;
    }

    internal static void ActivateExistingWindow()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName(current.ProcessName))
            {
                using (process)
                {
                    if (process.Id == current.Id) continue;
                    var hwnd = process.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;
                    if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
                    SetForegroundWindow(hwnd);
                    return;
                }
            }
        }
        catch
        {
            // Best-effort foreground activation only.
        }
    }

    public void Dispose()
    {
        if (ownsMutex)
        {
            ownsMutex = false;
            try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        mutex.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);
}
