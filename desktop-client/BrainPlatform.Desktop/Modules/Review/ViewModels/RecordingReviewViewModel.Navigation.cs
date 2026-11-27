namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed partial class RecordingReviewViewModel
{
    private Task? prefetchTask;
    private bool isDisposing;
    private Task? playbackLoadTask;
    private readonly LatestThrottledTask viewportLoader = new(TimeSpan.FromMilliseconds(50));
    private bool isTimelineDragging;
    private bool resumePlaybackAfterTimelineDrag;
    private double? pendingPreviewViewportStart;

    public void BeginTimelineDrag()
    {
        if (isTimelineDragging || isDisposing) return;
        isTimelineDragging = true;
        resumePlaybackAfterTimelineDrag = playback.IsPlaying;
        var wasCompleted = playback.IsCompleted;
        playback.Pause();
        PublishPlaybackChanges(resumePlaybackAfterTimelineDrag, wasCompleted);
        FanOutPlaybackActive(false);
        foreach (var slot in OpenAnalysisSlots)
        {
            if (!slot.IsSpectralInspection) continue;
            CancelSpectralWindowLoad(slot);
            if (slot.AlgorithmId == SpectralValuesPanelId)
            {
                CancelSpectralProjection();
                SpectralValues.Clear("游标拖动中，等待目标窗口。");
            }
            if (slot.AlgorithmId == CustomFrequencyInspectionPanelId)
            {
                CancelCustomFrequencyProjection();
                CustomFrequencyInspection.Clear("游标拖动中，等待目标窗口。");
            }
        }
    }

    public void EndTimelineDrag()
    {
        if (!isTimelineDragging) return;
        isTimelineDragging = false;
        var shouldResume = resumePlaybackAfterTimelineDrag;
        resumePlaybackAfterTimelineDrag = false;
        if (isDisposing) return;
        var wasPlaying = playback.IsPlaying;
        var wasCompleted = playback.IsCompleted;
        if (shouldResume && !playback.IsCompleted) playback.Play();
        PublishPlaybackChanges(wasPlaying, wasCompleted);
        FanOutCursorDragCompleted();
        FanOutPlaybackActive(playback.IsPlaying);
    }

    public Task SeekAsync(double positionSeconds)
    {
        SetPreviewPosition(positionSeconds);
        return session.LoadViewportAsync(ViewportStartSeconds, playback.PositionSeconds);
    }

    public Task SeekRelativeAsync(double seconds) => SeekAsync(PositionSeconds + seconds);

    public Task SeekPageAsync(int direction) => SeekRelativeAsync(direction * VisibleDurationSeconds);

    public async Task NavigateFromTrackClickAsync(double positionSeconds)
    {
        var targetPosition = Math.Clamp(positionSeconds, 0, DurationSeconds);
        var targetViewportStart = GetFollowViewportStart(targetPosition);
        if (await session.NavigateFromTrackClickAsync(targetViewportStart, targetPosition))
            SetPreviewPosition(targetPosition);
    }

    /// <summary>Cache hits apply immediately; cold targets use the bounded loader.</summary>
    public void PreviewSeek(double positionSeconds)
    {
        var targetViewportStart = GetFollowViewportStart(Math.Clamp(positionSeconds, 0, DurationSeconds));
        // Keep the chart viewport on the last complete frame while dragging.
        // Moving the axis before the replacement frame is ready exposes the
        // old frame only over its overlap and leaves the rest blank.
        FanOutPlaybackActive(false);
        SetPreviewPositionCore(positionSeconds, updateViewport: false, requestAnalysis: false);
        pendingPreviewViewportStart = targetViewportStart;
        if (session.PreviewNavigationTarget(targetViewportStart, playback.PositionSeconds))
        {
            pendingPreviewViewportStart = null;
            UpdateViewportStart(targetViewportStart);
            return;
        }

        ScheduleViewportLoad(targetViewportStart);
    }

    public void TogglePlayback()
    {
        if (isDisposing) return;
        var wasPlaying = playback.IsPlaying;
        var wasCompleted = playback.IsCompleted;
        if (playback.IsPlaying) playback.Pause();
        else { CancelEventPlacement(); playback.Play(); }
        PublishPlaybackChanges(wasPlaying, wasCompleted);
        FanOutPlaybackActive(playback.IsPlaying && !isTimelineDragging);
        if (playback.IsPlaying)
        {
            foreach (var slot in OpenAnalysisSlots)
                if (slot.IsSpectralInspection) PrepareUpcomingSpectralWindows(slot);
        }
        else
        {
            foreach (var slot in OpenAnalysisSlots)
                if (slot.IsSpectralInspection) CancelSpectralWindowLoad(slot);
        }
    }

    public void CyclePlaybackSpeed()
    {
        if (isDisposing) return;
        var next = playback.PlaybackRate switch { 0.5d => 1d, 1d => 2d, _ => 0.5d };
        playback.SetPlaybackRate(next);
        RaisePropertyChanged(nameof(PlaybackSpeedText));
    }

    public void TickPlayback()
    {
        var wasPlaying = playback.IsPlaying;
        var wasCompleted = playback.IsCompleted;
        if (isDisposing || isTimelineDragging || !playback.Update()) return;
        UpdateViewportStart(GetFollowViewportStart(playback.PositionSeconds));
        RaisePropertyChanged(nameof(PositionSeconds));
        RaisePropertyChanged(nameof(PositionTimeText));
        PublishPlaybackChanges(wasPlaying, wasCompleted);
        if (!playback.IsPlaying)
        {
            foreach (var slot in OpenAnalysisSlots)
                if (slot.IsSpectralInspection) StopSpectralPreparation(slot);
        }
        FanOutPlaybackActive(playback.IsPlaying && !isTimelineDragging);
        if (isDisposing) return;
        if (!isTimelineDragging) FanOutPlaybackAdvanced();
        if (isDisposing) return;
        if (!session.ContainsViewport(ViewportStartSeconds) &&
            !session.TryActivateCachedViewport(ViewportStartSeconds, playback.PositionSeconds))
            StartPlaybackLoad();
        PrefetchWhenApproachingCacheEdge();
    }

    private void ScheduleViewportLoad(double? requestedViewportStart = null)
    {
        var targetViewportStart = requestedViewportStart ?? pendingPreviewViewportStart ?? ViewportStartSeconds;
        viewportLoader.Schedule(async cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!session.TryActivateCachedViewport(targetViewportStart, playback.PositionSeconds))
                await session.LoadViewportAsync(targetViewportStart, playback.PositionSeconds);

            if (!cancellationToken.IsCancellationRequested &&
                pendingPreviewViewportStart is { } pending &&
                Math.Abs(pending - targetViewportStart) < 0.000_001 &&
                session.ContainsViewport(targetViewportStart))
            {
                pendingPreviewViewportStart = null;
                UpdateViewportStart(targetViewportStart);
            }
        });
    }

    private void StartPlaybackLoad()
    {
        // One foreground load fills a reusable block while the clock advances.
        if (playbackLoadTask is { IsCompleted: false }) return;
        playbackLoadTask = session.LoadViewportAsync(ViewportStartSeconds, playback.PositionSeconds);
    }

    private void PrefetchWhenApproachingCacheEdge()
    {
        var leadSeconds = Math.Max(5, VisibleDurationSeconds * 0.75);
        if (isTimelineDragging || session.IsLoading || !session.ContainsViewport(ViewportStartSeconds) ||
            session.CurrentFrame is not { } frame ||
            frame.WindowEndSeconds - (ViewportStartSeconds + VisibleDurationSeconds) > leadSeconds ||
            frame.WindowEndSeconds >= DurationSeconds || prefetchTask is { IsCompleted: false }) return;
        prefetchTask = session.PrefetchNextAsync();
    }

    private void SetPreviewPosition(double positionSeconds) => SetPreviewPositionCore(positionSeconds, true);

    private void SetPreviewPositionCore(double positionSeconds, bool updateViewport, bool requestAnalysis = true)
    {
        var wasPlaying = playback.IsPlaying;
        var wasCompleted = playback.IsCompleted;
        playback.SeekSeconds(positionSeconds);
        if (updateViewport)
            UpdateViewportStart(GetFollowViewportStart(playback.PositionSeconds));
        RaisePropertyChanged(nameof(PositionSeconds));
        RaisePropertyChanged(nameof(PositionTimeText));
        PublishPlaybackChanges(wasPlaying, wasCompleted);
        if (requestAnalysis)
        {
            FanOutCursorSelected();
        }
        else
        {
            foreach (var slot in OpenAnalysisSlots)
            {
                if (!slot.IsSpectralInspection) continue;
                CancelSpectralWindowLoad(slot);
                if (slot.AlgorithmId == SpectralValuesPanelId)
                {
                    CancelSpectralProjection();
                    SpectralValues.Clear("游标拖动中，等待目标窗口。");
                }
                if (slot.AlgorithmId == CustomFrequencyInspectionPanelId)
                {
                    CancelCustomFrequencyProjection();
                    CustomFrequencyInspection.Clear("游标拖动中，等待目标窗口。");
                }
            }
        }
    }

    private void FanOutPlaybackActive(bool active)
    {
        var generation = playback.Snapshot.Generation;
        foreach (var slot in OpenAnalysisSlots) slot.Binding?.SetPlaybackActive(active, generation);
    }

    private void FanOutCursorDragCompleted()
    {
        var generation = playback.Snapshot.Generation;
        foreach (var slot in OpenAnalysisSlots)
        {
            if (slot.IsSpectralInspection) _ = RequestSpectralWindowAsync(slot, PositionSeconds);
            else slot.Binding?.OnCursorDragCompleted(PositionSeconds, generation);
        }
    }

    private void FanOutPlaybackAdvanced()
    {
        var generation = playback.Snapshot.Generation;
        foreach (var slot in OpenAnalysisSlots)
        {
            if (slot.IsSpectralInspection) _ = RequestSpectralWindowAsync(slot, PositionSeconds, playbackAdvance: true);
            else slot.Binding?.OnPlaybackCursorAdvanced(PositionSeconds, generation);
        }
    }

    private void FanOutCursorSelected()
    {
        var generation = playback.Snapshot.Generation;
        foreach (var slot in OpenAnalysisSlots)
        {
            if (slot.IsSpectralInspection) _ = RequestSpectralWindowAsync(slot, PositionSeconds);
            else slot.Binding?.OnCursorPositionSelected(PositionSeconds, generation);
        }
    }

    private double GetFollowViewportStart(double positionSeconds) => Math.Clamp(
        positionSeconds - VisibleDurationSeconds * 0.5, 0, Math.Max(0, DurationSeconds - VisibleDurationSeconds));

    private void UpdateViewportStart(double value)
    {
        var bounded = Math.Clamp(value, 0, Math.Max(0, DurationSeconds - VisibleDurationSeconds));
        if (Math.Abs(viewportStartSeconds - bounded) < 0.000_001) return;
        viewportStartSeconds = bounded;
        RaisePropertyChanged(nameof(ViewportStartSeconds));
        RaisePropertyChanged(nameof(WindowTimeText));
    }

    private void ScheduleViewportDurationUpdate()
    {
        if ((HorizontalTimeScaleMode == HorizontalTimeScaleMode.PaperSpeed && displaySettings.ViewportWidthDips <= 0) ||
            viewportDurationUpdateTask is { IsCompleted: false })
        {
            if (viewportDurationUpdateTask is { IsCompleted: false }) viewportDurationUpdatePending = true;
            return;
        }
        var duration = Math.Clamp(displaySettings.GetVisibleSeconds(displaySettings.ViewportWidthDips), 1d, DurationSeconds);
        if (Math.Abs(duration - VisibleDurationSeconds) >= 0.02)
            viewportDurationUpdateTask = UpdateVisibleDurationAsync(duration);
    }

    private async Task UpdateVisibleDurationAsync(double visibleDuration)
    {
        try { await session.SetVisibleDurationAsync(visibleDuration, playback.PositionSeconds); }
        finally
        {
            viewportDurationUpdateTask = null;
            var rerun = viewportDurationUpdatePending;
            viewportDurationUpdatePending = false;
            if (rerun) ScheduleViewportDurationUpdate();
        }
    }
}
