namespace BrainPlatform.Desktop.Shared.Time;

public sealed record PlaybackCursorSnapshot(
    string RecordingId,
    long Generation,
    long CursorSample,
    long DurationSamples,
    int SamplingRateHz,
    double CursorSeconds,
    bool IsPlaying,
    double PlaybackRate,
    bool IsCompleted);

/// <summary>
/// Owns playback time for one recording workspace. Subscribers must enqueue
/// their own I/O or rendering work instead of blocking cursor publication.
/// </summary>
public sealed class RecordingPlaybackTimeCoordinator : IDisposable
{
    private readonly Func<double> clockSeconds;
    private double playAnchorClockSeconds;
    private long playAnchorSample;
    private double playbackRate = 1d;
    private bool disposed;
    private bool isPlaying;
    private bool isCompleted;
    private long generation;
    private long cursorSample;

    public RecordingPlaybackTimeCoordinator(
        string recordingId,
        int samplingRateHz,
        long durationSamples,
        Func<double>? clockSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(recordingId))
            throw new ArgumentException("recordingId is required", nameof(recordingId));
        if (samplingRateHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(samplingRateHz));
        if (durationSamples <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationSamples));

        RecordingId = recordingId;
        SamplingRateHz = samplingRateHz;
        DurationSamples = durationSamples;
        this.clockSeconds = clockSeconds ?? (() => Environment.TickCount64 / 1000d);
        Snapshot = CreateSnapshot();
    }

    public string RecordingId { get; }

    public int SamplingRateHz { get; }

    public long DurationSamples { get; }

    public double DurationSeconds => DurationSamples / (double)SamplingRateHz;

    public double PositionSeconds => Snapshot.CursorSeconds;

    public bool IsPlaying => Snapshot.IsPlaying;

    public bool IsCompleted => Snapshot.IsCompleted;

    public double PlaybackRate => Snapshot.PlaybackRate;

    public PlaybackCursorSnapshot Snapshot { get; private set; }

    public event Action<PlaybackCursorSnapshot>? CursorChanged;

    public void Play()
    {
        ThrowIfDisposed();
        if (isPlaying) return;
        if (isCompleted)
        {
            generation++;
            cursorSample = 0;
            isCompleted = false;
            Publish();
        }

        playAnchorClockSeconds = clockSeconds();
        playAnchorSample = cursorSample;
        isPlaying = true;
        Publish();
    }

    public void Pause()
    {
        ThrowIfDisposed();
        if (!isPlaying) return;
        Update();
        isPlaying = false;
        Publish();
    }

    public void SeekSeconds(double seconds)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        var sample = RecordingTimeMapper.ToCursorSample(
            Math.Clamp(seconds, 0, DurationSeconds), SamplingRateHz, DurationSamples);
        SeekSample(sample);
    }

    public void SeekSample(long sample)
    {
        ThrowIfDisposed();
        if (sample < 0 || sample > DurationSamples)
            throw new ArgumentOutOfRangeException(nameof(sample));

        generation++;
        cursorSample = sample;
        isCompleted = sample >= DurationSamples;
        playAnchorSample = sample;
        playAnchorClockSeconds = clockSeconds();
        Publish();
    }

    public void SetPlaybackRate(double rate)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(rate) || rate <= 0)
            throw new ArgumentOutOfRangeException(nameof(rate));
        if (Math.Abs(playbackRate - rate) < double.Epsilon) return;

        if (isPlaying) Update();
        playbackRate = rate;
        playAnchorSample = cursorSample;
        playAnchorClockSeconds = clockSeconds();
        Publish();
    }

    public bool Update()
    {
        ThrowIfDisposed();
        if (!isPlaying) return false;

        var elapsedSeconds = Math.Max(0, clockSeconds() - playAnchorClockSeconds);
        var elapsedSamples = elapsedSeconds * SamplingRateHz * playbackRate;
        var nextSample = Math.Min(DurationSamples, playAnchorSample + (long)Math.Floor(elapsedSamples));
        var changed = nextSample != cursorSample;
        cursorSample = nextSample;
        if (cursorSample >= DurationSamples)
        {
            isPlaying = false;
            isCompleted = true;
        }

        if (changed || isCompleted) Publish();
        return changed;
    }

    public void Reset()
    {
        ThrowIfDisposed();
        generation++;
        isPlaying = false;
        isCompleted = false;
        cursorSample = 0;
        playAnchorSample = 0;
        playAnchorClockSeconds = clockSeconds();
        Publish();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        isPlaying = false;
        CursorChanged = null;
    }

    private PlaybackCursorSnapshot CreateSnapshot() => new(
        RecordingId,
        generation,
        cursorSample,
        DurationSamples,
        SamplingRateHz,
        cursorSample / (double)SamplingRateHz,
        isPlaying,
        playbackRate,
        isCompleted);

    private void Publish()
    {
        Snapshot = CreateSnapshot();
        CursorChanged?.Invoke(Snapshot);
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(RecordingPlaybackTimeCoordinator));
    }
}
