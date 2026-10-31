namespace BrainPlatform.Desktop.Shared.Waveform;

public readonly record struct WaveformDisplayRequestContext(
    Guid RecordingId,
    string SourceRevision,
    long ViewGeneration,
    Guid RequestId)
{
    public bool IsValid => RecordingId != Guid.Empty &&
                           !string.IsNullOrWhiteSpace(SourceRevision) &&
                           ViewGeneration > 0 && RequestId != Guid.Empty;
}

public sealed record WaveformDisplayFrame<TPayload>(
    WaveformDisplayRequestContext Request,
    TPayload Payload);

public static class WaveformDisplayFramePublisher
{
    public static bool TryPublish<TPayload>(
        WaveformDisplayFrame<TPayload> frame,
        WaveformDisplayRequestContext currentRequest,
        CancellationToken cancellationToken,
        Action<TPayload> publish)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(publish);

        if (cancellationToken.IsCancellationRequested ||
            !frame.Request.IsValid ||
            frame.Request != currentRequest)
            return false;

        publish(frame.Payload);
        return true;
    }
}
