using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.Review;

namespace BrainPlatform.Desktop.ViewModels;

public sealed class RecordingReviewViewModel : ObservableObject, IAsyncDisposable
{
    private readonly RecordingReviewSession session;
    private readonly RecordingMontageCatalogResult catalog;
    private readonly RecordingPlaybackController playback;
    private readonly int samplingRateHz;
    private readonly int sourceChannelCount;
    private readonly string projectName;
    private readonly string recordingName;
    private readonly DateTimeOffset recordingStartUtc;
    private Task? prefetchTask;
    private Task? playbackLoadTask;
    private readonly LatestThrottledTask viewportLoader = new(TimeSpan.FromMilliseconds(50));
    private readonly LatestDelayedTask filterUpdater = new();
    private double highPassHz = 1;
    private double lowPassHz = 30;
    private double notchHz = 50;
    private double sensitivityMicrovoltsPerMillimeter = 10;
    private double paperSpeedMillimetersPerSecond = 30;
    private double viewportWidthDips;
    private Task? viewportDurationUpdateTask;
    private double viewportStartSeconds;
    private RecordingReviewFrame? publishedFrame;

    public RecordingReviewViewModel(
        IRecordingReviewReader reader,
        RecordingMontageCatalogResult catalog,
        double visibleDurationSeconds = 10,
        string? recordingName = null,
        string? projectName = null,
        IRecordingReviewFilter? filter = null,
        Func<double>? playbackClockSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
        session = new RecordingReviewSession(reader, catalog, visibleDurationSeconds, filter);
        playback = new RecordingPlaybackController(reader.DurationSeconds, playbackClockSeconds);
        samplingRateHz = reader.Manifest.SamplingRateHz;
        recordingStartUtc = reader.Manifest.RecordingStartUtc;
        sourceChannelCount = reader.Manifest.Channels.Count(channel =>
            channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar);
        this.projectName = string.IsNullOrWhiteSpace(projectName)
            ? reader.Manifest.Project.Name
            : projectName;
        this.recordingName = string.IsNullOrWhiteSpace(recordingName)
            ? "采集记录"
            : recordingName;
        session.Changed += OnSessionChanged;
        foreach (var item in catalog.CompatibleViewingMontages)
        {
            CompatibleViewingMontages.Add(item.Profile);
        }
    }

    public ObservableCollection<MontageProfile> CompatibleViewingMontages { get; } = [];

    public string AcquisitionMontageText => catalog.AcquisitionMontageStatusText;

    public MontageProfile? AcquisitionMontage => session.AcquisitionMontage;

    public MontageProfile? CurrentViewingMontage => session.SelectedViewingMontage;

    public RecordingReviewFrame? CurrentFrame => session.CurrentFrame;

    public double PositionSeconds => playback.PositionSeconds;

    /// <summary>The left edge shown by the chart. This changes at render cadence while navigating.</summary>
    public double ViewportStartSeconds => viewportStartSeconds;

    public double DurationSeconds => session.DurationSeconds;

    public int SamplingRateHz => samplingRateHz;

    public int SourceChannelCount => sourceChannelCount;

    public string ProjectName => projectName;

    public string RecordingName => recordingName;

    public DateTimeOffset RecordingStartUtc => recordingStartUtc;

    public double VisibleDurationSeconds => session.VisibleDurationSeconds;

    public int OutputChannelCount => session.CurrentFrame?.OutputChannelNames.Count ?? 0;

    public bool IsLoading => session.IsLoading;

    public bool IsPlaying => playback.IsPlaying;

    public bool IsCompleted => playback.IsCompleted;

    public string PlaybackSpeedText => $"{playback.PlaybackRate:0.0#}X";

    public string StatusText => session.StatusText;

    public string RecordingStartText => FormatRecordedTime(0, "HH:mm:ss.fff");

    public string RecordingEndText => FormatRecordedTime(DurationSeconds, "HH:mm:ss.fff");

    public string PositionTimeText => FormatRecordedTime(PositionSeconds, "HH:mm:ss.fff");

    public string WindowTimeText
    {
        get
        {
            var end = Math.Min(DurationSeconds, ViewportStartSeconds + VisibleDurationSeconds);
            return $"{FormatRecordedTime(ViewportStartSeconds, "HH:mm:ss.f")} - {FormatRecordedTime(end, "HH:mm:ss.f")}";
        }
    }

    public double HighPassHz
    {
        get => highPassHz;
        set
        {
            if (SetProperty(ref highPassHz, value))
            {
                ScheduleFilterUpdate();
            }
        }
    }

    public double LowPassHz
    {
        get => lowPassHz;
        set
        {
            if (SetProperty(ref lowPassHz, value))
            {
                ScheduleFilterUpdate();
            }
        }
    }

    public double NotchHz
    {
        get => notchHz;
        set
        {
            if (SetProperty(ref notchHz, value))
            {
                ScheduleFilterUpdate();
            }
        }
    }

    public double SensitivityMicrovoltsPerMillimeter
    {
        get => sensitivityMicrovoltsPerMillimeter;
        set
        {
            if (value is not (5d or 10d or 20d or 50d or 100d))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetProperty(ref sensitivityMicrovoltsPerMillimeter, value);
        }
    }

    public double PaperSpeedMillimetersPerSecond
    {
        get => paperSpeedMillimetersPerSecond;
        set
        {
            if (value is not (5d or 10d or 15d or 30d or 60d))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (SetProperty(ref paperSpeedMillimetersPerSecond, value))
            {
                ScheduleViewportDurationUpdate();
            }
        }
    }

    public async Task InitializeAsync()
    {
        await session.InitializeAsync();
        UpdateViewportStart(session.ViewportStartSeconds);
    }

    public Task SeekAsync(double positionSeconds)
    {
        SetPreviewPosition(positionSeconds);
        return session.LoadViewportAsync(ViewportStartSeconds, playback.PositionSeconds);
    }

    public Task SeekRelativeAsync(double seconds) => SeekAsync(PositionSeconds + seconds);

    public async Task NavigateFromTrackClickAsync(double positionSeconds)
    {
        var targetPosition = Math.Clamp(positionSeconds, 0, DurationSeconds);
        var targetViewportStart = GetFollowViewportStart(targetPosition);
        if (!await session.NavigateFromTrackClickAsync(targetViewportStart, targetPosition))
        {
            return;
        }

        // The session has atomically published the target frame and viewport.
        // Move the playback cursor only after that publication, so a track
        // click never produces a slider position with an older waveform.
        playback.Seek(targetPosition);
        RaisePropertyChanged(nameof(PositionSeconds));
        RaisePropertyChanged(nameof(PositionTimeText));
        RaisePlaybackPropertiesChanged();
    }

    public Task SeekPageAsync(int direction) => SeekRelativeAsync(direction * VisibleDurationSeconds);

    /// <summary>
    /// Dragging updates the accepted scrollbar position continuously. Loading is
    /// throttled rather than debounced, so it starts before mouse-up.
    /// </summary>
    public void PreviewSeek(double positionSeconds)
    {
        SetPreviewPosition(positionSeconds);
        ScheduleViewportLoad();
        PrefetchWhenApproachingCacheEdge();
    }

    public Task SetVisibleDurationAsync(double visibleDurationSeconds) =>
        session.SetVisibleDurationAsync(visibleDurationSeconds, playback.PositionSeconds);

    public void UpdateViewportWidth(double widthDips)
    {
        if (!double.IsFinite(widthDips) || widthDips <= 0 || Math.Abs(widthDips - viewportWidthDips) < 0.5)
        {
            return;
        }

        viewportWidthDips = widthDips;
        ScheduleViewportDurationUpdate();
    }

    public Task SelectViewingMontageAsync(MontageProfile? montage) =>
        session.SelectViewingMontageAsync(montage, playback.PositionSeconds);

    public void TogglePlayback()
    {
        if (playback.IsPlaying)
        {
            playback.Pause();
        }
        else
        {
            playback.Play();
        }

        RaisePlaybackPropertiesChanged();
    }

    public void CyclePlaybackSpeed()
    {
        var next = playback.PlaybackRate switch
        {
            0.5d => 1d,
            1d => 2d,
            _ => 0.5d,
        };
        playback.SetPlaybackRate(next);
        RaisePropertyChanged(nameof(PlaybackSpeedText));
    }

    public void TickPlayback()
    {
        if (!playback.Update())
        {
            return;
        }

        RaisePropertyChanged(nameof(PositionSeconds));
        RaisePropertyChanged(nameof(PositionTimeText));
        RaisePlaybackPropertiesChanged();
        var desiredViewportStart = GetFollowViewportStart(playback.PositionSeconds);
        UpdateViewportStart(desiredViewportStart);
        if (!session.ContainsViewport(desiredViewportStart) &&
            !session.TryActivateCachedViewport(desiredViewportStart, playback.PositionSeconds))
        {
            StartPlaybackLoad();
        }

        PrefetchWhenApproachingCacheEdge();
    }

    public async ValueTask DisposeAsync()
    {
        viewportLoader.Dispose();
        filterUpdater.Dispose();
        session.Changed -= OnSessionChanged;
        await session.DisposeAsync();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        RaisePropertyChanged(nameof(AcquisitionMontageText));
        RaisePropertyChanged(nameof(CurrentViewingMontage));
        if (!ReferenceEquals(publishedFrame, session.CurrentFrame))
        {
            publishedFrame = session.CurrentFrame;
            RaisePropertyChanged(nameof(CurrentFrame));
        }
        UpdateViewportStart(session.ViewportStartSeconds);
        RaisePropertyChanged(nameof(WindowTimeText));
        RaisePropertyChanged(nameof(VisibleDurationSeconds));
        RaisePropertyChanged(nameof(OutputChannelCount));
        RaisePropertyChanged(nameof(IsLoading));
        RaisePropertyChanged(nameof(StatusText));
        RaisePlaybackPropertiesChanged();
    }

    private void RaisePlaybackPropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsPlaying));
        RaisePropertyChanged(nameof(IsCompleted));
    }

    private void ScheduleFilterUpdate()
    {
        filterUpdater.Schedule(TimeSpan.FromMilliseconds(220), async cancellationToken =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await session.SetFilterAsync(new RecordingReviewFilterSettings(
                    HighPassHz, LowPassHz, NotchHz <= 0 ? null : NotchHz), playback.PositionSeconds);
            }
            catch (ArgumentException)
            {
                // A transient invalid pair can occur while two selectors update.
            }
        });
    }

    private void ScheduleViewportLoad()
    {
        viewportLoader.Schedule(async cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await session.LoadViewportAsync(ViewportStartSeconds, playback.PositionSeconds);
        });
    }

    private void StartPlaybackLoad()
    {
        // Do not debounce repeatedly at render cadence. One foreground request
        // keeps running while playback advances; it fills a reusable cache block.
        if (playbackLoadTask is { IsCompleted: false })
        {
            return;
        }

        playbackLoadTask = LoadPlaybackBlockAsync(ViewportStartSeconds, playback.PositionSeconds);
    }

    private async Task LoadPlaybackBlockAsync(double viewportStart, double position)
    {
        await session.LoadViewportAsync(viewportStart, position);
    }

    private void PrefetchWhenApproachingCacheEdge()
    {
        var leadSeconds = Math.Max(5, VisibleDurationSeconds * 0.75);
        if (session.IsLoading || !session.ContainsViewport(ViewportStartSeconds) || session.CurrentFrame is not { } frame ||
            frame.WindowEndSeconds - (ViewportStartSeconds + VisibleDurationSeconds) > leadSeconds ||
            frame.WindowEndSeconds >= DurationSeconds ||
            prefetchTask is { IsCompleted: false })
        {
            return;
        }

        prefetchTask = session.PrefetchNextAsync();
    }

    private void SetPreviewPosition(double positionSeconds)
    {
        playback.Seek(positionSeconds);
        UpdateViewportStart(GetFollowViewportStart(playback.PositionSeconds));
        RaisePropertyChanged(nameof(PositionSeconds));
        RaisePropertyChanged(nameof(PositionTimeText));
        RaisePlaybackPropertiesChanged();
    }
    private double GetFollowViewportStart(double positionSeconds)
    {
        var maximum = Math.Max(0, DurationSeconds - VisibleDurationSeconds);
        return Math.Clamp(positionSeconds - VisibleDurationSeconds * 0.25, 0, maximum);
    }
    private void UpdateViewportStart(double value)
    {
        var bounded = Math.Clamp(value, 0, Math.Max(0, DurationSeconds - VisibleDurationSeconds));
        if (Math.Abs(viewportStartSeconds - bounded) < 0.000_001)
            return;

        viewportStartSeconds = bounded;
        RaisePropertyChanged(nameof(ViewportStartSeconds));
        RaisePropertyChanged(nameof(WindowTimeText));
    }
    private void ScheduleViewportDurationUpdate()
    {
        if (viewportWidthDips <= 0 || viewportDurationUpdateTask is { IsCompleted: false })
        {
            return;
        }

        var viewportMillimeters = viewportWidthDips * 25.4d / 96d;
        var visibleDuration = Math.Clamp(
            viewportMillimeters / PaperSpeedMillimetersPerSecond,
            1d,
            DurationSeconds);
        if (Math.Abs(visibleDuration - VisibleDurationSeconds) < 0.02)
        {
            return;
        }

        viewportDurationUpdateTask = UpdateVisibleDurationAsync(visibleDuration);
    }
    private async Task UpdateVisibleDurationAsync(double visibleDuration)
    {
        try
        {
            await session.SetVisibleDurationAsync(visibleDuration, playback.PositionSeconds);
        }
        finally
        {
            viewportDurationUpdateTask = null;
            if (viewportWidthDips > 0)
            {
                ScheduleViewportDurationUpdate();
            }
        }
    }

    private string FormatRecordedTime(double relativeSeconds, string format) =>
        recordingStartUtc
            .AddTicks(ToTicks(Math.Clamp(relativeSeconds, 0, DurationSeconds)))
            .ToLocalTime()
            .ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    private static long ToTicks(double seconds) =>
        checked((long)Math.Round(seconds * TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero));
}
