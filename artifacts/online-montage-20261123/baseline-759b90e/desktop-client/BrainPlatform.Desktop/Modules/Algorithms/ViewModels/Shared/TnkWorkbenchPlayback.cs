using System.Diagnostics;
using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed partial class TnkDetailViewModel
{
    private readonly TnkWorkbenchPlaybackDiagnostics playbackDiagnostics = new();
    private long lastPlaybackTarget = -1;
    public IReadOnlyList<double> PlaybackSpeeds { get; } = [1, 2, 4];
    private RecordingPlaybackTimeCoordinator? Playback => Catalog.PlaybackCoordinator;
    public double PlaybackSpeed
    {
        get => Playback?.PlaybackRate ?? 1d;
        set
        {
            if (!PlaybackSpeeds.Contains(value)) return;
            if (Playback is null) return;
            Playback.SetPlaybackRate(value);
            RaisePropertyChanged();
        }
    }
    public string PlaybackButtonText => Playback?.IsPlaying == true ? "暂停" : "播放";
    public ICommand TogglePlaybackCommand { get; private set; } = null!;
    public ICommand RestartPlaybackCommand { get; private set; } = null!;
    private void InitializePlayback()
    {
        TogglePlaybackCommand = new RelayCommand(() =>
        {
            if (Playback is null || RecordingDuration <= 0) return;
            if (Playback.IsPlaying) StopWorkbenchPlayback();
            else
            {
                Catalog.PauseDynamicPreviewCommand.Execute(null);
                Playback.Play();
                playbackDiagnostics.Reset();
                lastPlaybackTarget = -1;
                RaisePropertyChanged(nameof(PlaybackButtonText));
            }
        });
        RestartPlaybackCommand = new RelayCommand(() => { StopWorkbenchPlayback(); SeekFromChart(0); });
    }

    private void OnPlaybackRendering(object? sender, EventArgs args)
    {
        if (!workbenchActive || Playback is null || !Playback.Update()) return;
        var tickStarted = Stopwatch.GetTimestamp();
        var positionUpdateStarted = tickStarted;
        WaveformPosition = Playback.PositionSeconds;
        var positionUpdateCompleted = Stopwatch.GetTimestamp();
        if (Waveform is { TimeSeconds.Count: > 0 } signal &&
            (WaveformPosition < signal.TimeSeconds[0] || WaveformPosition > signal.TimeSeconds[^1] - 7) &&
            signal.TimeSeconds[^1] < RecordingDuration - .1 && waveformRequest is null)
            ReloadWaveform();
        var targetSecond = (long)Math.Floor(WaveformPosition);
        var analysisScheduleStarted = Stopwatch.GetTimestamp();
        if (Catalog.IsOnDemandAnalysis && targetSecond != lastPlaybackTarget)
        {
            lastPlaybackTarget = targetSecond;
            Catalog.QueueDynamicPreviewSecondsForPlayback(WaveformPosition);
        }
        var analysisScheduleCompleted = Stopwatch.GetTimestamp();
        if (Playback.IsCompleted) RaisePropertyChanged(nameof(PlaybackButtonText));
        playbackDiagnostics.Record(Catalog.RegisteredRecording?.Id ?? "unknown",
            Catalog.RegisteredRecording?.SamplingRateHz ?? 0, PlaybackSpeed, tickStarted,
            Stopwatch.GetTimestamp(), WaveformPosition, Catalog.IsOnDemandAnalysis,
            Stopwatch.GetElapsedTime(positionUpdateStarted, positionUpdateCompleted).TotalMilliseconds,
            Stopwatch.GetElapsedTime(analysisScheduleStarted, analysisScheduleCompleted).TotalMilliseconds,
            targetSecond);
    }
    private void StopWorkbenchPlayback()
    {
        Playback?.Pause();
        RaisePropertyChanged(nameof(PlaybackButtonText));
    }
}
