using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class PreviewDisplayContinuityTests
{
    [Fact]
    public void ImpedanceInterruption_FreezesVisibleTraceAndResumesOnDisconnectedSegmentAcrossRepeatedSwitches()
    {
        var state = Snapshot(AcquisitionState.Previewing);
        AcquisitionStreamMetadata? metadata = Metadata();
        IReadOnlyList<AcquisitionBatch> batches = [Batch(1_000, 10)];
        using var monitor = CreateMonitor(() => state, () => metadata, () => batches);
        monitor.UpdateViewportWidth(1_000);
        monitor.Refresh();
        monitor.FreezePreviewDisplayForImpedance();

        state = Snapshot(AcquisitionState.Stopped);
        metadata = null;
        batches = [];
        monitor.Refresh();
        var frozen = Assert.IsType<LiveWaveformSource>(monitor.GetWaveformSource());
        Assert.True(monitor.IsDisplayFrozen);
        Assert.Equal("阻抗检测中，EEG 已停止；波形已冻结", monitor.CaptureStatusText);
        Assert.Equal(1_000, frozen.Batches[0].FirstSampleCounter);

        state = Snapshot(AcquisitionState.Previewing);
        metadata = Metadata();
        batches = [Batch(0, 10)];
        monitor.ResumePreviewDisplay();

        var resumed = Assert.IsType<LiveWaveformSource>(monitor.GetWaveformSource());
        Assert.Equal([1_000L, 1_011L], resumed.Batches.Select(batch => batch.FirstSampleCounter));
        var frame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(resumed, 1, 200));
        Assert.Contains(frame.Traces[0].Points, point => point.SampleCounter == 1_011 && point.StartsSegment);

        batches = [Batch(0, 20)];
        monitor.Refresh();
        _ = monitor.GetWaveformSource();
        monitor.FreezePreviewDisplayForImpedance();
        state = Snapshot(AcquisitionState.Stopped);
        metadata = null;
        batches = [];
        monitor.Refresh();
        state = Snapshot(AcquisitionState.Previewing);
        metadata = Metadata();
        batches = [Batch(0, 10)];
        monitor.ResumePreviewDisplay();

        resumed = Assert.IsType<LiveWaveformSource>(monitor.GetWaveformSource());
        Assert.Equal([1_000L, 1_011L, 1_032L], resumed.Batches.Select(batch => batch.FirstSampleCounter));
        frame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(resumed, 1, 200));
        Assert.Contains(frame.Traces[0].Points, point => point.SampleCounter == 1_032 && point.StartsSegment);
    }

    [Theory]
    [InlineData(500, false)]
    [InlineData(1_000, true)]
    public void ImpedanceInterruption_DoesNotCombineIncompatibleStreamLayout(int samplingRateHz, bool changeChannelLayout)
    {
        var state = Snapshot(AcquisitionState.Previewing);
        AcquisitionStreamMetadata? metadata = Metadata();
        IReadOnlyList<AcquisitionBatch> batches = [Batch(1_000, 10)];
        using var monitor = CreateMonitor(() => state, () => metadata, () => batches);
        monitor.UpdateViewportWidth(1_000);
        monitor.Refresh();
        monitor.FreezePreviewDisplayForImpedance();

        state = Snapshot(AcquisitionState.Stopped);
        metadata = null;
        batches = [];
        monitor.Refresh();
        state = Snapshot(AcquisitionState.Previewing);
        metadata = Metadata() with
        {
            SamplingRateHz = samplingRateHz,
            Channels = changeChannelLayout
                ? Metadata().Channels.Select((channel, index) => index == 0
                    ? channel with { NativeChannelIndex = 77 }
                    : channel).ToArray()
                : Metadata().Channels,
        };
        batches = [Batch(0, 10)];
        monitor.ResumePreviewDisplay();

        var source = Assert.IsType<LiveWaveformSource>(monitor.GetWaveformSource());
        Assert.Single(source.Batches);
        Assert.Equal(0, source.Batches[0].FirstSampleCounter);
    }

    private static AcquisitionBatch Batch(long firstSampleCounter, int sampleCount) =>
        new(firstSampleCounter, sampleCount, 12, new double[sampleCount * 12], DateTimeOffset.UtcNow);

    private static LiveMonitoringViewModel CreateMonitor(
        Func<AcquisitionStateSnapshot> stateProvider,
        Func<AcquisitionStreamMetadata?> metadataProvider,
        Func<IReadOnlyList<AcquisitionBatch>> batchesProvider) =>
        new(stateProvider, metadataProvider, batchesProvider, Dispatcher.CurrentDispatcher);

    private static AcquisitionStateSnapshot Snapshot(AcquisitionState state) =>
        new(state, "test", Guid.NewGuid(), DateTimeOffset.UtcNow);

    private static AcquisitionStreamMetadata Metadata() => new(
        "test-device",
        "test device",
        1_000,
        Enumerable.Range(0, 12).Select(index => new AcquisitionChannel(
            index,
            index,
            $"EEG{index}",
            AcquisitionChannelKind.Reference,
            "V")).ToArray(),
        11,
        DateTimeOffset.UtcNow);
}
