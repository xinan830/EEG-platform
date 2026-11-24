using System.IO;

namespace BrainPlatform.Desktop.Modules.Devices.Drivers.AntEego;

/// <summary>
/// Owns the vendor-global SDK lifetime. The SDK documentation permits only one
/// live stream, so this runtime also serializes every native operation.
/// </summary>
internal static class AntEegoNativeRuntime
{
    private static readonly AntEegoNativeRuntimeState shared = new(AntEegoNativeApi.Load);

    public static AntEegoNativeLease Acquire(string sdkLibraryPath, bool forStream) => shared.Acquire(sdkLibraryPath, forStream);

    public static void Shutdown() => shared.Shutdown();
}

internal sealed class AntEegoNativeRuntimeState(Func<string, AntEegoNativeApi> loadApi)
{
    private readonly object gate = new();
    private readonly AntEegoNativeStreamGate streamGate = new();
    private AntEegoNativeApi? api;
    private string? loadedPath;
    private int leaseCount;
    private bool shutdown;

    public AntEegoNativeLease Acquire(string sdkLibraryPath, bool forStream)
    {
        var fullPath = Path.GetFullPath(sdkLibraryPath);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(shutdown, this);
            if (forStream && streamGate.IsOccupied) streamGate.Acquire();
            if (streamGate.IsOccupied || (forStream && leaseCount != 0))
                throw new AcquisitionDeviceBusyException("设备正在使用或检测，请稍后重试。");
            if (api is null)
            {
                api = loadApi(fullPath);
                loadedPath = fullPath;
            }
            else if (!string.Equals(loadedPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new AntEegoSdkException(
                    "ANT_EEGO_SDK_PATH_CONFLICT",
                    "Another ANT/eego SDK path is already active in this desktop process.");
            }

            if (forStream)
            {
                streamGate.Acquire();
            }

            leaseCount++;

            return new AntEegoNativeLease(api, forStream, Release);
        }
    }

    public void Shutdown()
    {
        lock (gate)
        {
            if (shutdown) return;
            if (leaseCount != 0)
            {
                throw new InvalidOperationException("Cannot shut down the ANT/eego SDK while native leases are active.");
            }

            shutdown = true;
            var activeApi = api;
            api = null;
            loadedPath = null;
            activeApi?.Dispose();
        }
    }

    private void Release(bool wasStream)
    {
        lock (gate)
        {
            if (wasStream)
            {
                streamGate.Release();
            }

            leaseCount--;
            if (leaseCount < 0)
            {
                leaseCount = 0;
                throw new InvalidOperationException("ANT/eego native lease was released more than once.");
            }
        }
    }
}

internal sealed class AntEegoNativeLease : IDisposable
{
    private static readonly AntEegoNativeOperationGate sdkOperationGate = new();
    private readonly Action<bool> release;
    private readonly bool isStreamLease;
    private bool disposed;

    public AntEegoNativeLease(AntEegoNativeApi api, bool isStreamLease, Action<bool> release)
    {
        Api = api;
        this.isStreamLease = isStreamLease;
        this.release = release;
    }

    public AntEegoNativeApi Api { get; }

    public T Invoke<T>(Func<AntEegoNativeApi, T> operation)
    {
        return sdkOperationGate.Invoke(() =>
        {
            ThrowIfDisposed();
            return operation(Api);
        });
    }

    public void Invoke(Action<AntEegoNativeApi> operation)
    {
        sdkOperationGate.Invoke(() =>
        {
            ThrowIfDisposed();
            operation(Api);
        });
    }

    public void Dispose()
    {
        sdkOperationGate.Invoke(() =>
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            release(isStreamLease);
        });
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}

internal sealed class AntEegoNativeOperationGate
{
    private readonly object gate = new();

    public T Invoke<T>(Func<T> operation)
    {
        lock (gate)
        {
            return operation();
        }
    }

    public void Invoke(Action operation)
    {
        lock (gate)
        {
            operation();
        }
    }
}
