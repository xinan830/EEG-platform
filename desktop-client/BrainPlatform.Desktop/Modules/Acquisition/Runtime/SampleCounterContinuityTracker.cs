using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Acquisition.Runtime;

public sealed record ContinuityObservation(AcquisitionGap? Gap, long NextExpectedSampleCounter);

/// <summary>
/// Detects gaps from the device's sample counter. PC arrival time is recorded
/// separately and is never used as the scientific time axis.
/// </summary>
public sealed class SampleCounterContinuityTracker
{
    private long? nextExpectedSampleCounter;

    public ContinuityObservation Observe(AcquisitionBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        AcquisitionGap? gap = null;
        if (nextExpectedSampleCounter is { } expected)
        {
            if (batch.FirstSampleCounter < expected)
            {
                throw new AcquisitionContinuityException(
                    $"Received sample counter {batch.FirstSampleCounter} after {expected - 1}; " +
                    "the stream is out of order or its counter was reset.");
            }

            if (batch.FirstSampleCounter > expected)
            {
                gap = new AcquisitionGap(
                    expected,
                    batch.FirstSampleCounter - 1,
                    batch.FirstSampleCounter - expected,
                    batch.ReceivedAtUtc);
            }
        }

        nextExpectedSampleCounter = checked(batch.LastSampleCounter + 1);
        return new ContinuityObservation(gap, nextExpectedSampleCounter.Value);
    }
}
