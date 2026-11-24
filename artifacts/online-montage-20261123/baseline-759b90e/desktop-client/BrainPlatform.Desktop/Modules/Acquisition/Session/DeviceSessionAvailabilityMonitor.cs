using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace BrainPlatform.Desktop.Modules.Acquisition.Session;

/// <summary>
/// Uses Windows device-change notifications to request a debounced SDK
/// confirmation, with a low-frequency idle probe as a fallback. Windows is a
/// trigger only; DeviceSessionManager remains the hardware fact owner.
/// </summary>
public sealed class DeviceSessionAvailabilityMonitor : IDisposable
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDevNodesChanged = 0x0007;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;
    private readonly DeviceSessionManager deviceSession;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer fallbackTimer;
    private readonly DispatcherTimer deviceChangeDebounceTimer;
    private readonly CancellationTokenSource cancellationSource = new();
    private Window? attachedWindow;
    private HwndSource? hwndSource;
    private bool refreshInProgress;
    private bool refreshRequestedWhileBusy;
    private bool disposed;

    public DeviceSessionAvailabilityMonitor(
        DeviceSessionManager deviceSession,
        Dispatcher dispatcher,
        TimeSpan? fallbackInterval = null,
        TimeSpan? deviceChangeDebounce = null)
    {
        this.deviceSession = deviceSession ?? throw new ArgumentNullException(nameof(deviceSession));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        fallbackTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = fallbackInterval ?? TimeSpan.FromSeconds(20),
        };
        fallbackTimer.Tick += OnFallbackTimerTick;
        deviceChangeDebounceTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = deviceChangeDebounce ?? TimeSpan.FromMilliseconds(750),
        };
        deviceChangeDebounceTimer.Tick += OnDeviceChangeDebounceTimerTick;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        fallbackTimer.Start();
    }

    /// <summary>
    /// Hooks the application's existing WPF window. Device-change messages do
    /// not establish connectivity themselves; they only schedule SDK probing.
    /// </summary>
    public void Attach(Window window)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(window);
        if (attachedWindow is not null && !ReferenceEquals(attachedWindow, window))
        {
            throw new InvalidOperationException("设备状态监视器只能附加到一个主窗口。");
        }

        attachedWindow = window;
        if (PresentationSource.FromVisual(window) is HwndSource source)
        {
            AttachSource(source);
            return;
        }

        window.SourceInitialized += OnWindowSourceInitialized;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        fallbackTimer.Stop();
        fallbackTimer.Tick -= OnFallbackTimerTick;
        deviceChangeDebounceTimer.Stop();
        deviceChangeDebounceTimer.Tick -= OnDeviceChangeDebounceTimerTick;
        if (attachedWindow is not null)
        {
            attachedWindow.SourceInitialized -= OnWindowSourceInitialized;
        }
        if (hwndSource is not null)
        {
            hwndSource.RemoveHook(OnWindowMessage);
        }
        cancellationSource.Cancel();
        cancellationSource.Dispose();
    }

    private void OnWindowSourceInitialized(object? sender, EventArgs eventArgs)
    {
        if (!disposed && sender is Window window && PresentationSource.FromVisual(window) is HwndSource source)
        {
            AttachSource(source);
        }
    }

    private void AttachSource(HwndSource source)
    {
        if (hwndSource is not null)
        {
            return;
        }

        hwndSource = source;
        hwndSource.AddHook(OnWindowMessage);
    }

    private IntPtr OnWindowMessage(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (!disposed && message == WmDeviceChange)
        {
            var changeKind = unchecked((int)(long)wParam);
            if (changeKind is DbtDevNodesChanged or DbtDeviceArrival or DbtDeviceRemoveComplete)
            {
                deviceChangeDebounceTimer.Stop();
                deviceChangeDebounceTimer.Start();
            }
        }

        return IntPtr.Zero;
    }

    private void OnFallbackTimerTick(object? sender, EventArgs eventArgs) => RequestRefresh();

    private void OnDeviceChangeDebounceTimerTick(object? sender, EventArgs eventArgs)
    {
        deviceChangeDebounceTimer.Stop();
        RequestRefresh();
    }

    private void RequestRefresh()
    {
        if (disposed)
        {
            return;
        }

        if (refreshInProgress)
        {
            refreshRequestedWhileBusy = true;
            return;
        }

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        refreshInProgress = true;
        try
        {
            await deviceSession.RefreshAvailabilityAsync(cancellationSource.Token);
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            // Application shutdown cancels an in-flight availability check.
        }
        finally
        {
            refreshInProgress = false;
            if (refreshRequestedWhileBusy && !disposed)
            {
                refreshRequestedWhileBusy = false;
                _ = dispatcher.BeginInvoke(RequestRefresh, DispatcherPriority.Background);
            }
        }
    }
}
