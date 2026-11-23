namespace BrainPlatform.Desktop.Modules.Devices.Drivers.AntEego;

internal sealed class AntEegoNativeStreamOwnership(
    Action closeStream,
    Action closeAmplifier,
    Action releaseLease,
    Action onReleased) : IAsyncDisposable
{
    private readonly SemaphoreSlim disposeGate = new(1, 1);
    private bool streamClosed;
    private bool amplifierClosed;
    private bool closing;
    private bool released;

    public bool IsClosing => Volatile.Read(ref closing);

    public bool IsReleased => Volatile.Read(ref released);

    public async ValueTask DisposeAsync()
    {
        await disposeGate.WaitAsync();
        try
        {
            if (released)
            {
                return;
            }

            Volatile.Write(ref closing, true);
            if (!streamClosed)
            {
                closeStream();
                streamClosed = true;
            }

            if (!amplifierClosed)
            {
                closeAmplifier();
                amplifierClosed = true;
            }

            releaseLease();
            Volatile.Write(ref released, true);
            onReleased();
        }
        finally
        {
            disposeGate.Release();
        }
    }
}
