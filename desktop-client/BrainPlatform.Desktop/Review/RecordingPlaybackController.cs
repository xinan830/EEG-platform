namespace BrainPlatform.Desktop.Review;

public sealed class RecordingPlaybackController
{
    private readonly Func<double> clockSeconds;
    private double playAnchorClockSeconds;
    private double playAnchorPositionSeconds;

    public RecordingPlaybackController(double durationSeconds, Func<double>? clockSeconds = null)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        }

        DurationSeconds = durationSeconds;
        this.clockSeconds = clockSeconds ?? (() => Environment.TickCount64 / 1000d);
    }

    public double DurationSeconds { get; }

    public double PositionSeconds { get; private set; }

    public bool IsPlaying { get; private set; }

    public bool IsCompleted { get; private set; }

    public void Play()
    {
        if (IsCompleted)
        {
            PositionSeconds = 0;
        }

        playAnchorClockSeconds = clockSeconds();
        playAnchorPositionSeconds = PositionSeconds;
        IsPlaying = true;
        IsCompleted = false;
    }

    public void Pause()
    {
        Update();
        IsPlaying = false;
    }

    public void Seek(double positionSeconds)
    {
        PositionSeconds = Math.Clamp(positionSeconds, 0, DurationSeconds);
        playAnchorPositionSeconds = PositionSeconds;
        playAnchorClockSeconds = clockSeconds();
        IsCompleted = PositionSeconds >= DurationSeconds;
    }

    public bool Update()
    {
        if (!IsPlaying)
        {
            return false;
        }

        var next = playAnchorPositionSeconds + Math.Max(0, clockSeconds() - playAnchorClockSeconds);
        PositionSeconds = Math.Min(DurationSeconds, next);
        if (PositionSeconds >= DurationSeconds)
        {
            IsPlaying = false;
            IsCompleted = true;
        }

        return true;
    }
}
