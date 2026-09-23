namespace BrainPlatform.Desktop.Modules.Review.Playback;

public sealed class RecordingPlaybackController
{
    private readonly Func<double> clockSeconds;
    private double playAnchorClockSeconds;
    private double playAnchorPositionSeconds;
    private double playbackRate = 1;

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

    public double PlaybackRate => playbackRate;

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

    public void SetPlaybackRate(double rate)
    {
        if (rate is not (0.5d or 1d or 2d))
        {
            throw new ArgumentOutOfRangeException(nameof(rate));
        }

        Update();
        playbackRate = rate;
        playAnchorPositionSeconds = PositionSeconds;
        playAnchorClockSeconds = clockSeconds();
    }

    public bool Update()
    {
        if (!IsPlaying)
        {
            return false;
        }

        var next = playAnchorPositionSeconds + Math.Max(0, clockSeconds() - playAnchorClockSeconds) * playbackRate;
        PositionSeconds = Math.Min(DurationSeconds, next);
        if (PositionSeconds >= DurationSeconds)
        {
            IsPlaying = false;
            IsCompleted = true;
        }

        return true;
    }
}
