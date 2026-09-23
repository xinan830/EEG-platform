using System.IO;

namespace BrainPlatform.Desktop.Acquisition.AntEego;

/// <summary>
/// Owns the vendor-global SDK lifetime. The SDK documentation permits only one
/// live stream, so this runtime also serializes every native operation.
/// </summary>
internal static class AntEegoNativeRuntime
{
    private static readonly object gate = new();
    private static AntEegoNativeApi? api;
    private static string? loadedPath;
    private static int leaseCount;
    private static bool activeStream;

    public static AntEegoNativeLease Acquire(string sdkLibraryPath, bool forStream)
    {
        var fullPath = Path.GetFullPath(sdkLibraryPath);
        lock (gate)
        {
            if (api is null)
            {
                api = AntEegoNativeApi.Load(fullPath);
                loadedPath = fullPath;
            }
            else if (!string.Equals(loadedPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new AntEegoSdkException(
                    "ANT_EEGO_SDK_PATH_CONFLICT",
                    "Another ANT/eego SDK path is already active in this desktop process.");
            }

            if (forStream && activeStream)
            {
                throw new AntEegoSdkException(
                    "ANT_EEGO_STREAM_ALREADY_ACTIVE",
                    "The ANT/eego SDK allows only one active stream per process.");
            }

            leaseCount++;
            if (forStream)
            {
                activeStream = true;
            }

            return new AntEegoNativeLease(api, forStream, Release);
        }
    }

    private static void Release(bool wasStream)
    {
        lock (gate)
        {
            if (wasStream)
            {
                activeStream = false;
            }

            leaseCount--;
            if (leaseCount < 0)
            {
                leaseCount = 0;
                throw new InvalidOperationException("ANT/eego native lease was released more than once.");
            }

            if (leaseCount == 0 && api is not null)
            {
                var activeApi = api;
                api = null;
                loadedPath = null;
                activeApi.Dispose();
            }
        }
    }
}

internal sealed class AntEegoNativeLease : IDisposable
{
    private static readonly object sdkOperationGate = new();
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
        lock (sdkOperationGate)
        {
            ThrowIfDisposed();
            return operation(Api);
        }
    }

    public void Invoke(Action<AntEegoNativeApi> operation)
    {
        lock (sdkOperationGate)
        {
            ThrowIfDisposed();
            operation(Api);
        }
    }

    public void Dispose()
    {
        lock (sdkOperationGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            release(isStreamLease);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
