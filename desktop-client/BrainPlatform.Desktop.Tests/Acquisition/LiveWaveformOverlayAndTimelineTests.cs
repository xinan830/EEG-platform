using System.Windows;
using System.Windows.Shapes;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Acquisition.ViewModels;
using BrainPlatform.Desktop.Modules.Acquisition.Waveform;
using BrainPlatform.Desktop.Modules.Events.Domain;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class LiveWaveformOverlayAndTimelineTests
{
    [Fact]
    public void AnnotationOverlay_PreservesLifecycleBoundariesWhenEventAddedOnSameRevision()
    {
        RunOnDispatcher(() =>
        {
            var startUtc = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            var metadata = new AcquisitionStreamMetadata(
                "dev-1", "Test", 500,
                [new AcquisitionChannel(0, 0, "Fp1", AcquisitionChannelKind.Reference, "V")],
                -1, startUtc);
            var events = new List<RecordingEvent>();
            var boundaries = new List<RecordingLifecycleBoundary>
            {
                new(RecordingLifecycleBoundaryKind.Started, 100, startUtc),
            };
            using var monitor = new LiveMonitoringViewModel(
                () => new AcquisitionStateSnapshot(AcquisitionState.Recording, "Recording", null, startUtc),
                () => metadata,
                () => [new AcquisitionBatch(100, 500, 1, new double[500], startUtc)],
                Dispatcher.CurrentDispatcher,
                recordingFirstSampleCounterProvider: () => 100,
                recordingEventsProvider: () => events,
                recordingStartUtcProvider: () => startUtc,
                lifecycleBoundariesProvider: () => boundaries);

            var frame = new WaveformDisplayFrame(
                WindowStartSampleCounter: 100,
                WindowSampleCount: 1000,
                DisplayWindowSeconds: 2.0,
                SamplingRateHz: 500,
                PageStartElapsedSeconds: 0.0,
                CursorSeconds: 1.0,
                Traces: []);
            var overlay = new LiveWaveformAnnotationOverlay();
            var plotArea = new Rect(0, 0, 800, 400);

            Assert.True(overlay.Update(frame, monitor, plotArea));
            Assert.Single(overlay.Visual.Children.OfType<Line>());

            // Adding an event on the same WaveformDisplayFrame must keep lifecycle boundary lines intact.
            events.Add(new RecordingEvent(
                "evt-1", "rec-1", "def-1",
                new EventDefinitionSnapshot("E1", "Blink", "#2563EB", 1, EventKind.Point),
                EventSource.ManualButton, "test", null, 200, 0, startUtc, startUtc));

            Assert.True(overlay.Update(frame, monitor, plotArea));
            Assert.Single(overlay.Visual.Children.OfType<Line>());
            Assert.NotEmpty(overlay.Visual.Children.OfType<System.Windows.Controls.Border>());

            // Unchanged state skips redundant re-render.
            Assert.False(overlay.Update(frame, monitor, plotArea));
        });
    }

    [Fact]
    public void TimelineTracker_ExcludesPausedSamplesFromDurationAndMapsRecordingCoordinates()
    {
        var tracker = new LiveDisplayTimelineTracker();
        var startUtc = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var metadata = new AcquisitionStreamMetadata(
            "dev-1", "Test", 500,
            [new AcquisitionChannel(0, 0, "Fp1", AcquisitionChannelKind.Reference, "V")],
            -1, startUtc);

        tracker.OnMetadataChanged(true, startUtc);
        tracker.Update(AcquisitionState.Recording, [new AcquisitionBatch(100, 500, 1, new double[500], startUtc)], 100);
        Assert.Equal(TimeSpan.FromSeconds(1), tracker.GetEffectiveAcquisitionDuration(metadata,
            [new AcquisitionBatch(100, 500, 1, new double[500], startUtc)], 100));

        // Pause after sample 599, resume at sample 1600 (1000 paused samples excluded).
        tracker.Update(AcquisitionState.Paused, [new AcquisitionBatch(100, 500, 1, new double[500], startUtc)], 100);
        var afterResume = new AcquisitionBatch(1600, 500, 1, new double[500], startUtc.AddSeconds(3));
        tracker.Update(AcquisitionState.Recording, [afterResume], 100);

        Assert.Equal(TimeSpan.FromSeconds(2), tracker.GetEffectiveAcquisitionDuration(metadata, [afterResume], 100));
        Assert.Equal(150L, tracker.ToDisplayCounterFromRecordingSample(50, 100));
        Assert.Null(tracker.ToDisplayCounterFromRecordingSample(-1, 100));
    }

    [Fact]
    public void TimelineTracker_ContinuousPreviewDuringPause_FreezesDurationAndComputesExactEffectiveSeconds()
    {
        var tracker = new LiveDisplayTimelineTracker();
        var startUtc = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var metadata = new AcquisitionStreamMetadata(
            "dev-1", "Test", 500,
            [new AcquisitionChannel(0, 0, "Fp1", AcquisitionChannelKind.Reference, "V")],
            -1, startUtc);

        tracker.OnMetadataChanged(true, startUtc);
        var b1 = new AcquisitionBatch(0, 500, 1, new double[500], startUtc);
        tracker.Update(AcquisitionState.Recording, [b1], 0);
        Assert.Equal(TimeSpan.FromSeconds(1), tracker.GetEffectiveAcquisitionDuration(metadata, [b1], 0));

        // Pause for 2s while preview batches keep arriving and Refresh runs repeatedly.
        var b2 = new AcquisitionBatch(500, 500, 1, new double[500], startUtc.AddSeconds(1));
        tracker.Update(AcquisitionState.Paused, [b1, b2], 0);
        Assert.Equal(TimeSpan.FromSeconds(1), tracker.GetEffectiveAcquisitionDuration(metadata, [b1, b2], 0));

        var b3 = new AcquisitionBatch(1000, 500, 1, new double[500], startUtc.AddSeconds(2));
        tracker.Update(AcquisitionState.Paused, [b1, b2, b3], 0);
        Assert.Equal(TimeSpan.FromSeconds(1), tracker.GetEffectiveAcquisitionDuration(metadata, [b1, b2, b3], 0));

        // Resume and record 1s -> total effective recording must be 2s (not 4s).
        var b4 = new AcquisitionBatch(1500, 500, 1, new double[500], startUtc.AddSeconds(3));
        tracker.Update(AcquisitionState.Recording, [b1, b2, b3, b4], 0);
        Assert.Equal(TimeSpan.FromSeconds(2), tracker.GetEffectiveAcquisitionDuration(metadata, [b1, b2, b3, b4], 0));

        // Second pause + resume must accumulate cleanly to 3s.
        var b5 = new AcquisitionBatch(2000, 500, 1, new double[500], startUtc.AddSeconds(4));
        tracker.Update(AcquisitionState.Paused, [b1, b2, b3, b4, b5], 0);
        Assert.Equal(TimeSpan.FromSeconds(2), tracker.GetEffectiveAcquisitionDuration(metadata, [b1, b2, b3, b4, b5], 0));

        var b6 = new AcquisitionBatch(2500, 500, 1, new double[500], startUtc.AddSeconds(5));
        tracker.Update(AcquisitionState.Recording, [b1, b2, b3, b4, b5, b6], 0);
        Assert.Equal(TimeSpan.FromSeconds(3), tracker.GetEffectiveAcquisitionDuration(metadata, [b1, b2, b3, b4, b5, b6], 0));
    }

    [Fact]
    public void LiveMonitoringViewModel_ContinuousPauseWithLifecycleBoundaries_KeepsWaveformContinuousAndFreezesRecordingElapsed()
    {
        RunOnDispatcher(() =>
        {
            var startUtc = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            var metadata = new AcquisitionStreamMetadata(
                "dev-1", "Test", 500,
                [new AcquisitionChannel(0, 0, "Fp1", AcquisitionChannelKind.Reference, "V")],
                -1, startUtc);
            var state = new AcquisitionStateSnapshot(AcquisitionState.Recording, "Recording", null, startUtc);
            var batches = new List<AcquisitionBatch>
            {
                new(0, 500, 1, new double[500], startUtc),
            };
            var boundaries = new List<RecordingLifecycleBoundary>
            {
                new(RecordingLifecycleBoundaryKind.Started, 0, startUtc),
            };

            using var monitor = new LiveMonitoringViewModel(
                () => state,
                () => metadata,
                () => batches.ToArray(),
                Dispatcher.CurrentDispatcher,
                recordingFirstSampleCounterProvider: () => 0,
                recordingStartUtcProvider: () => startUtc,
                lifecycleBoundariesProvider: () => boundaries.ToArray());

            monitor.Refresh();
            Assert.Equal("00:00:01", monitor.RecordingElapsedText);

            state = new AcquisitionStateSnapshot(AcquisitionState.Paused, "Paused", null, startUtc.AddSeconds(1));
            boundaries.Add(new(RecordingLifecycleBoundaryKind.Paused, 499, startUtc.AddSeconds(1)));
            batches.Add(new(500, 500, 1, new double[500], startUtc.AddSeconds(1)));
            monitor.Refresh();
            Assert.Equal("00:00:01", monitor.RecordingElapsedText);

            batches.Add(new(1000, 500, 1, new double[500], startUtc.AddSeconds(2)));
            monitor.Refresh();
            Assert.Equal("00:00:01", monitor.RecordingElapsedText);

            state = new AcquisitionStateSnapshot(AcquisitionState.Recording, "Recording", null, startUtc.AddSeconds(3));
            boundaries.Add(new(RecordingLifecycleBoundaryKind.Resumed, 1500, startUtc.AddSeconds(3)));
            batches.Add(new(1500, 500, 1, new double[500], startUtc.AddSeconds(3)));
            monitor.Refresh();
            Assert.Equal("00:00:02", monitor.RecordingElapsedText);

            state = state with { State = AcquisitionState.Paused };
            boundaries.Add(new(RecordingLifecycleBoundaryKind.Paused, 1999, startUtc.AddSeconds(4)));
            batches.Add(new(2000, 500, 1, new double[500], startUtc.AddSeconds(4)));
            monitor.Refresh();
            Assert.Equal("00:00:02", monitor.RecordingElapsedText);

            // Resume has no boundary until the first resumed recording batch arrives.
            state = state with { State = AcquisitionState.Recording };
            monitor.Refresh();
            _ = monitor.GetWaveformSource();
            Assert.Equal("00:00:02", monitor.RecordingElapsedText);
            boundaries.Add(new(RecordingLifecycleBoundaryKind.Resumed, 2500, startUtc.AddSeconds(5)));
            batches.Add(new(2500, 500, 1, new double[500], startUtc.AddSeconds(5)));
            monitor.Refresh();
            Assert.Equal("00:00:03", monitor.RecordingElapsedText);

            var source = Assert.IsType<LiveWaveformSource>(monitor.GetWaveformSource());
            Assert.Empty(source.DisplayCounterAdjustments!);
        });
    }

    [Theory]
    [InlineData(0L, false)]
    [InlineData(0L, true)]
    [InlineData(10_000L, false)]
    [InlineData(10_000L, true)]
    public void RecordingBeforeFirstBatch_DoesNotCountPreviewOrInitialPause(long previewFirst, bool useBoundaries)
    {
        RunOnDispatcher(() =>
        {
            var startUtc = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            var metadata = new AcquisitionStreamMetadata(
                "dev-1", "Test", 500,
                [new AcquisitionChannel(0, 0, "Fp1", AcquisitionChannelKind.Reference, "V")], -1, startUtc);
            var state = new AcquisitionStateSnapshot(AcquisitionState.Previewing, "Preview", null, startUtc);
            long? recordingFirst = null;
            var batches = new List<AcquisitionBatch>
            {
                new(previewFirst, 5_000, 1, new double[5_000], startUtc),
            };
            var boundaries = new List<RecordingLifecycleBoundary>();
            using var monitor = new LiveMonitoringViewModel(
                () => state, () => metadata, () => batches.ToArray(), Dispatcher.CurrentDispatcher,
                recordingFirstSampleCounterProvider: () => recordingFirst,
                lifecycleBoundariesProvider: () => boundaries.ToArray());

            void AssertElapsed(string expected)
            {
                monitor.Refresh();
                Assert.Equal(expected, monitor.RecordingElapsedText);
                var source = Assert.IsType<LiveWaveformSource>(monitor.GetWaveformSource());
                Assert.Equal(expected, monitor.RecordingElapsedText);
                Assert.Equal(previewFirst, source.SessionFirstSampleCounter);
                Assert.Empty(source.DisplayCounterAdjustments!);
            }

            state = state with { State = AcquisitionState.Recording };
            AssertElapsed("00:00:00");
            state = state with { State = AcquisitionState.Paused };
            AssertElapsed("00:00:00");
            batches.Add(new(previewFirst + 5_000, 1_000, 1, new double[1_000], startUtc.AddSeconds(10)));
            AssertElapsed("00:00:00");
            state = state with { State = AcquisitionState.Recording };
            AssertElapsed("00:00:00");

            recordingFirst = previewFirst + 6_000;
            if (useBoundaries)
            {
                boundaries.Add(new(RecordingLifecycleBoundaryKind.Started, recordingFirst.Value, startUtc.AddSeconds(12)));
                boundaries.Add(new(RecordingLifecycleBoundaryKind.Resumed, recordingFirst.Value, startUtc.AddSeconds(12)));
            }
            batches.Add(new(recordingFirst.Value, 500, 1, new double[500], startUtc.AddSeconds(12)));
            AssertElapsed("00:00:01");
        });
    }

    private static void RunOnDispatcher(Action test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { test(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        completion.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        thread.Join();
    }
}
