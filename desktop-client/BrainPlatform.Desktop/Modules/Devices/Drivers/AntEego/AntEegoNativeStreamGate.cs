namespace BrainPlatform.Desktop.Modules.Devices.Drivers.AntEego;

internal sealed class AntEegoNativeStreamGate
{
    private readonly object gate = new();
    private bool occupied;

    public bool IsOccupied
    {
        get
        {
            lock (gate)
            {
                return occupied;
            }
        }
    }

    public void Acquire()
    {
        lock (gate)
        {
            if (occupied)
            {
                throw new AntEegoSdkException(
                    "ANT_EEGO_STREAM_ALREADY_ACTIVE",
                    "The ANT/eego SDK allows only one active stream per process.");
            }

            occupied = true;
        }
    }

    public void Release()
    {
        lock (gate)
        {
            if (!occupied)
            {
                throw new InvalidOperationException("ANT/eego stream ownership was released more than once.");
            }

            occupied = false;
        }
    }
}
