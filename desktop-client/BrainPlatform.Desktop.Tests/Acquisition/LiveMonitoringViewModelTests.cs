using System.Windows.Threading;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Views;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class LiveMonitoringViewModelTests
{
    [Fact]
    public void Refresh_UsesOnlyActualEegChannelsAndShowsTheFirstEightByDefault()
    {
        var state = Snapshot(AcquisitionState.Recording);
        var metadata = Metadata();
        using var monitor = CreateMonitor(() => state, () => metadata);

        monitor.Refresh();

        Assert.Equal(10, monitor.Channels.Count);
        Assert.Equal(8, monitor.Channels.Count(channel => channel.IsVisible));
        Assert.Equal("Fp1", monitor.Channels[0].Label);
        Assert.Equal("Bipolar", monitor.Channels[^1].Kind);
        Assert.Equal("正在记录真实 EEG", monitor.CaptureStatusText);
        Assert.True(monitor.IsRecording);
    }

    [Fact]
    public void RecordingRelativeEventCoordinate_MapsBackToTheNonZeroDeviceCounter()
    {
        using var monitor = CreateMonitor(
            () => Snapshot(AcquisitionState.Recording),
            () => Metadata(),
            recordingFirstSampleCounterProvider: () => 12_000);

        Assert.Equal(12_345, monitor.ToDisplayCounterFromRecordingSample(345));
    }

    [Fact]
    public void DisplayOptions_UsePhysicalPaperSpeedAndRejectUnsupportedValues()
    {
        using var monitor = CreateMonitor(
            () => Snapshot(AcquisitionState.Ready),
            () => Metadata());

        monitor.PaperSpeedMillimetersPerSecond = 30;
        monitor.SensitivityMicrovoltsPerMillimeter = 20;

        Assert.Equal(30, monitor.PaperSpeedMillimetersPerSecond);
        Assert.Equal(20, monitor.SensitivityMicrovoltsPerMillimeter);
        var widthDipsForThirtyMillimeters = 30d * 96d / 25.4d;
        Assert.Equal(1, monitor.GetDisplayWindowSeconds(widthDipsForThirtyMillimeters), precision: 12);
        monitor.PaperSpeedMillimetersPerSecond = 15;
        Assert.Equal(2, monitor.GetDisplayWindowSeconds(widthDipsForThirtyMillimeters), precision: 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => monitor.PaperSpeedMillimetersPerSecond = 20);
        Assert.Throws<ArgumentOutOfRangeException>(() => monitor.SensitivityMicrovoltsPerMillimeter = 0);
    }

    [Fact]
    public void TimebaseMode_UsesExactSecondsPerScreenWithoutChangingPaperSpeedPreference()
    {
        using var monitor = CreateMonitor(
            () => Snapshot(AcquisitionState.Ready),
            () => Metadata());

        monitor.UpdateViewportWidth(960);
        monitor.PaperSpeedMillimetersPerSecond = 30;
        monitor.TimebaseSecondsPerScreen = 15;
        monitor.HorizontalTimeScaleMode = HorizontalTimeScaleMode.Timebase;

        Assert.False(monitor.IsPaperSpeedMode);
        Assert.True(monitor.IsTimebaseMode);
        Assert.Equal(15, monitor.GetDisplayWindowSeconds(320), precision: 12);
        Assert.Equal(30, monitor.PaperSpeedMillimetersPerSecond);

        monitor.HorizontalTimeScaleMode = HorizontalTimeScaleMode.PaperSpeed;
        Assert.Equal(254d / 30d, monitor.GetDisplayWindowSeconds(960), precision: 12);
    }

    [Fact]
    public void PaperSpeed_UsesTheOwningWindowHorizontalScaleOnly()
    {
        using var monitor = CreateMonitor(() => Snapshot(AcquisitionState.Ready), () => Metadata());
        monitor.UpdateScreenScale(new ScreenScaleContext("wide", 0.5, 0.25));
        monitor.PaperSpeedMillimetersPerSecond = 30;

        Assert.Equal(16, monitor.GetDisplayWindowSeconds(960), precision: 12);
    }

    [Fact]
    public void Refresh_ShowsPausedStateAndKeepsElapsedRecordingTime()
    {
        var state = new AcquisitionStateSnapshot(
            AcquisitionState.Paused,
            "paused",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        var metadata = Metadata() with { RecordingStartUtc = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5) };
        using var monitor = CreateMonitor(() => state, () => metadata);

        monitor.Refresh();

        Assert.True(monitor.IsPaused);
        Assert.True(monitor.HasOpenRecording);
        Assert.False(monitor.IsRecording);
        Assert.Equal("记录已暂停，实时预览继续", monitor.CaptureStatusText);
        Assert.NotEqual("--:--:--", monitor.RecordingElapsedText);
    }

    [Fact]
    public void Refresh_ShowsPreviewWithoutPretendingRecordingHasStarted()
    {
        using var monitor = CreateMonitor(
            () => Snapshot(AcquisitionState.Previewing),
            () => Metadata(),
            () => [new AcquisitionBatch(0, 1_000, 12, new double[12_000], DateTimeOffset.UtcNow)]);

        monitor.Refresh();

        Assert.True(monitor.IsPreviewing);
        Assert.True(monitor.HasOpenStream);
        Assert.False(monitor.HasOpenRecording);
        Assert.False(monitor.IsRecording);
        Assert.Equal("实时预览中，尚未记录", monitor.CaptureStatusText);
        Assert.Equal("--:--:--", monitor.RecordingElapsedText);
    }

    [Fact]
    public void FaultedStream_ExposesTheActualFailureOnTheWaveformSurface()
    {
        var state = new AcquisitionStateSnapshot(
            AcquisitionState.Faulted,
            "设备样本计数器停止推进。",
            null,
            DateTimeOffset.UtcNow);
        using var monitor = CreateMonitor(() => state, () => null);

        monitor.Refresh();

        Assert.Equal("采集已停止：设备样本计数器停止推进。", monitor.WaveformStatusText);
    }

    [Fact]
    public void GetWaveformSource_ReturnsTheRetainedBatchesAndOnlyVisibleChannels()
    {
        var batches = new[]
        {
            new AcquisitionBatch(42, 2, 12, new double[24], DateTimeOffset.UtcNow),
        };
        using var monitor = CreateMonitor(
            () => Snapshot(AcquisitionState.Recording),
            () => Metadata(),
            () => batches);
        monitor.Refresh();
        monitor.Channels[0].IsVisible = false;

        var source = Assert.IsType<LiveWaveformSource>(monitor.GetWaveformSource());

        Assert.Same(batches, source.Batches);
        Assert.Equal(7, source.Channels.Count);
        Assert.DoesNotContain(source.Channels, channel => channel.Label == "Fp1");
    }

    [Fact]
    public void RecordingPause_DoesNotRewriteTheLiveDisplayTimeline()
    {
        var state = Snapshot(AcquisitionState.Recording);
        var metadata = Metadata();
        IReadOnlyList<AcquisitionBatch> batches =
        [
            new AcquisitionBatch(0, 1_000, 12, new double[12_000], DateTimeOffset.UtcNow),
        ];
        using var monitor = CreateMonitor(() => state, () => metadata, () => batches);
        monitor.Refresh();
        _ = monitor.GetWaveformSource();

        state = Snapshot(AcquisitionState.Paused);
        monitor.Refresh();
        Assert.Equal("00:00:01", monitor.RecordingElapsedText);

        state = Snapshot(AcquisitionState.Recording);
        batches =
        [
            batches[0],
            new AcquisitionBatch(6_000, 1_000, 12, new double[12_000], DateTimeOffset.UtcNow),
        ];
        monitor.Refresh();
        var source = Assert.IsType<LiveWaveformSource>(monitor.GetWaveformSource());
        var frame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(
            source,
            displayWindowSeconds: 10,
            horizontalPixels: 500));

        Assert.Empty(source.DisplayCounterAdjustments!);
        Assert.Equal(7, frame.CursorSeconds, precision: 10);
        Assert.Equal("00:00:02", monitor.RecordingElapsedText);
        var firstResumedPoint = frame.Traces[0].Points.First(point => point.SampleCounter >= 6_000);
        Assert.True(firstResumedPoint.StartsSegment);
    }

    [Fact]
    public void ConfigureChannels_ShowsSelectedConfiguredElectrodesBeforeAStreamStarts()
    {
        using var monitor = CreateMonitor(
            () => Snapshot(AcquisitionState.Ready),
            () => null);
        var rows = new[]
        {
            new ChannelLabelMappingRow(0, "Reference", "V", "Fp1", true),
            new ChannelLabelMappingRow(1, "Reference", "V", "Fp2", false),
            new ChannelLabelMappingRow(2, "Reference", "V", "Fz", true),
        };

        monitor.ConfigureChannels(rows);

        Assert.Equal(["Fp1", "Fz"], monitor.VisibleChannelLabels);
        Assert.Null(monitor.GetWaveformSource());
    }

    [Fact]
    public void ConfigureChannels_DoesNotAutoDisplayUnnamedPhysicalInputs()
    {
        var metadata = new AcquisitionStreamMetadata(
            "test-device",
            "test device",
            500,
            [
                new AcquisitionChannel(0, 0, "Fp1", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(1, 20, null, AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(2, 31, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
            ],
            2,
            DateTimeOffset.UtcNow);
        using var monitor = CreateMonitor(() => Snapshot(AcquisitionState.Ready), () => metadata);

        monitor.ConfigureChannels([new ChannelLabelMappingRow(0, "Reference", "V", "Fp1", true)]);
        monitor.Refresh();

        Assert.Equal(["Fp1"], monitor.VisibleChannelLabels);
        Assert.DoesNotContain(monitor.Channels, channel =>
            channel.IsVisible && channel.Label.StartsWith("CH ", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfigureMontage_UsesDerivedOutputsForTheSettingsChannelListAndWaveform()
    {
        var batches = new[]
        {
            new AcquisitionBatch(0, 10, 12, new double[120], DateTimeOffset.UtcNow),
        };
        using var monitor = CreateMonitor(
            () => Snapshot(AcquisitionState.Previewing),
            () => Metadata(),
            () => batches);
        monitor.Refresh();
        monitor.ConfigureMontage(CreateMontage());

        Assert.Equal(2, monitor.MontageOutputChannelCount);
        Assert.Equal(2, monitor.SelectedMontageDisplayChannelCount);
        Assert.Equal(["Fp1-REF", "F3-Fp1"], monitor.VisibleChannelLabels);

        monitor.MontageChannels.Single(channel => channel.Name == "F3-Fp1").IsVisible = false;
        var source = Assert.IsType<LiveWaveformSource>(monitor.GetWaveformSource());
        var frame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(source, 1, 100));

        Assert.Equal(1, monitor.SelectedMontageDisplayChannelCount);
        Assert.Equal(["Fp1-REF"], monitor.VisibleChannelLabels);
        Assert.Single(frame.Traces);
        Assert.Equal("Fp1-REF", frame.Traces[0].Label);
    }

    [Fact]
    public void WithoutMontage_SettingsDoesNotFabricateAMontageChannelCount()
    {
        using var monitor = CreateMonitor(() => Snapshot(AcquisitionState.Ready), () => Metadata());

        Assert.False(monitor.HasSelectedMontage);
        Assert.Equal("未选择导联配置", monitor.CurrentMontageName);
        Assert.Equal(0, monitor.MontageOutputChannelCount);
        Assert.Empty(monitor.MontageChannels);
    }

    private static MontageProfile CreateMontage()
    {
        var now = DateTimeOffset.UtcNow;
        var configuration = new ChannelConfigurationProfile(
            "channels", "通道", string.Empty, ChannelConfigurationSource.User, "device",
            [], now, now);
        var derived = new[]
        {
            new DerivedMontageChannel("Fp1-REF", "Fp1", MontageNegativeKind.OriginalHardwareReference, [], 0),
            new DerivedMontageChannel("F3-Fp1", "F3", MontageNegativeKind.Channel, ["Fp1"], 1),
        };
        return new MontageProfile(
            "montage", "测试导联", string.Empty, MontageProfileSource.User, "device", "fingerprint",
            configuration, derived, 1, now, now);
    }

    private static LiveMonitoringViewModel CreateMonitor(
        Func<AcquisitionStateSnapshot> stateProvider,
        Func<AcquisitionStreamMetadata?> metadataProvider,
        Func<IReadOnlyList<AcquisitionBatch>>? batchesProvider = null,
        Func<long?>? recordingFirstSampleCounterProvider = null) =>
        new(
            stateProvider,
            metadataProvider,
            batchesProvider ?? (() => []),
            Dispatcher.CurrentDispatcher,
            recordingFirstSampleCounterProvider: recordingFirstSampleCounterProvider);

    private static AcquisitionStateSnapshot Snapshot(AcquisitionState state) =>
        new(state, "test", Guid.NewGuid(), DateTimeOffset.UtcNow);

    private static AcquisitionStreamMetadata Metadata() =>
        new(
            "test-device",
            "test device",
            1_000,
            [
                new AcquisitionChannel(0, 0, "Fp1", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(1, 1, "Fp2", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(2, 2, "F3", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(3, 3, "F4", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(4, 4, "C3", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(5, 5, "C4", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(6, 6, "P3", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(7, 7, "P4", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(8, 24, "BIP 1", AcquisitionChannelKind.Bipolar, "V"),
                new AcquisitionChannel(9, 25, "BIP 2", AcquisitionChannelKind.Bipolar, "V"),
                new AcquisitionChannel(10, 28, "Trigger", AcquisitionChannelKind.Trigger, "code"),
                new AcquisitionChannel(11, 29, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
            ],
            11,
            DateTimeOffset.UtcNow);
}
