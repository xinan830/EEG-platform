using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed partial class TnkDetailViewModel
{
    private readonly DispatcherTimer workbenchTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch playbackClock = new();
    private readonly TnkWorkbenchPlaybackDiagnostics playbackDiagnostics = new();
    private double playbackOrigin;
    private double playbackSpeed = 1;
    private bool workbenchPlaying;
    private long lastPlaybackTarget = -1;
    public IReadOnlyList<double> PlaybackSpeeds { get; } = [1, 2, 4];
    public double PlaybackSpeed
    {
        get => playbackSpeed;
        set
        {
            if (!PlaybackSpeeds.Contains(value)) return;
            playbackOrigin = WaveformPosition; playbackClock.Restart();
            SetProperty(ref playbackSpeed, value);
        }
    }
    public string PlaybackButtonText => workbenchPlaying ? "暂停" : "播放";
    public ICommand TogglePlaybackCommand { get; private set; } = null!;
    public ICommand RestartPlaybackCommand { get; private set; } = null!;
    private void InitializePlayback()
    {
        TogglePlaybackCommand = new RelayCommand(() =>
        {
            if (workbenchPlaying) StopWorkbenchPlayback();
            else if (RecordingDuration > 0)
            {
                Catalog.PauseDynamicPreviewCommand.Execute(null);
                playbackOrigin = WaveformPosition; playbackClock.Restart(); workbenchPlaying = true;
                playbackDiagnostics.Reset();
                lastPlaybackTarget = -1;
                workbenchTimer.Start(); RaisePropertyChanged(nameof(PlaybackButtonText));
            }
        });
        RestartPlaybackCommand = new RelayCommand(() => { StopWorkbenchPlayback(); SeekFromChart(0); });
        workbenchTimer.Tick += (_, _) =>
        {
            var tickStarted = Stopwatch.GetTimestamp();
            var positionUpdateStarted = tickStarted;
            WaveformPosition = Math.Min(RecordingDuration, playbackOrigin + playbackClock.Elapsed.TotalSeconds * PlaybackSpeed);
            var positionUpdateCompleted = Stopwatch.GetTimestamp();
            if (Waveform is { TimeSeconds.Count: > 0 } signal &&
                (WaveformPosition < signal.TimeSeconds[0] || WaveformPosition > signal.TimeSeconds[^1] - 7) &&
                signal.TimeSeconds[^1] < RecordingDuration - .1 && waveformRequest is null)
                ReloadWaveform();
            // Display frames must not continuously reset the analysis seek debounce.
            var targetSecond = (long)Math.Floor(WaveformPosition);
            var analysisScheduleStarted = Stopwatch.GetTimestamp();
            if (Catalog.IsOnDemandAnalysis && targetSecond != lastPlaybackTarget)
            {
                lastPlaybackTarget = targetSecond;
                Catalog.QueueDynamicPreviewSecondsForPlayback(WaveformPosition);
            }
            var analysisScheduleCompleted = Stopwatch.GetTimestamp();
            if (WaveformPosition >= RecordingDuration - .002) StopWorkbenchPlayback();
            playbackDiagnostics.Record(Catalog.RegisteredRecording?.Id ?? "unknown",
                Catalog.RegisteredRecording?.SamplingRateHz ?? 0, PlaybackSpeed, tickStarted,
                Stopwatch.GetTimestamp(), WaveformPosition, Catalog.IsOnDemandAnalysis,
                Stopwatch.GetElapsedTime(positionUpdateStarted, positionUpdateCompleted).TotalMilliseconds,
                Stopwatch.GetElapsedTime(analysisScheduleStarted, analysisScheduleCompleted).TotalMilliseconds,
                targetSecond);
        };
    }
    private void StopWorkbenchPlayback()
    {
        workbenchTimer.Stop(); playbackClock.Stop(); workbenchPlaying = false;
        RaisePropertyChanged(nameof(PlaybackButtonText));
    }
}
