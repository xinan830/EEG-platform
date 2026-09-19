using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.ViewModels;
using BrainPlatform.Desktop.Views;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class WaveformDisplayFrameBuilderTests
{
    [Fact]
    public void Build_DecimatesRawVoltsAndMarksKnownCounterGapsAsNewSegments()
    {
        var frame = WaveformDisplayFrameBuilder.Build(
            Source(
                Batch(0, [1e-6, 2e-6, 3e-6, 4e-6]),
                Batch(6, [5e-6, 6e-6])),
            displayWindowSeconds: 1,
            horizontalPixels: 500);

        var trace = Assert.Single(Assert.IsType<WaveformDisplayFrame>(frame).Traces);

        Assert.Equal(6, trace.Points.Count);
        Assert.True(trace.Points[0].StartsSegment);
        Assert.True(trace.Points[4].StartsSegment);
        Assert.Equal(1e-6, trace.Points[0].MinVolts);
        Assert.Equal(6e-6, trace.Points[^1].MaxVolts);
        Assert.Equal(0.8, frame.CursorSeconds, precision: 10);
    }

    [Fact]
    public void Build_WrapsTheEraseCursorAndKeepsTheUnwrittenTailFromThePreviousPage()
    {
        var earlyFrame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(
            Source(Batch(0, Enumerable.Repeat(1e-6, 45).ToArray())),
            displayWindowSeconds: 10,
            horizontalPixels: 500));
        var fullFrame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(
            Source(Batch(0, Enumerable.Repeat(1e-6, 120).ToArray())),
            displayWindowSeconds: 10,
            horizontalPixels: 500));

        Assert.Equal(4.5, earlyFrame.CursorSeconds, precision: 10);
        Assert.Equal(2, fullFrame.CursorSeconds, precision: 10);
        Assert.Equal(100, fullFrame.WindowStartSampleCounter);

        var trace = Assert.Single(fullFrame.Traces);
        Assert.Equal(100, trace.Points.Count);
        Assert.Equal(Enumerable.Range(100, 20).Select(value => (long)value), trace.Points.Take(20).Select(point => point.SampleCounter));
        Assert.Equal(Enumerable.Range(20, 80).Select(value => (long)value), trace.Points.Skip(20).Select(point => point.SampleCounter));
        Assert.Equal(0, trace.Points[0].DisplaySampleOffset);
        Assert.Equal(19, trace.Points[19].DisplaySampleOffset);
        Assert.True(trace.Points[20].StartsSegment);
        Assert.Equal(20, trace.Points[20].DisplaySampleOffset);
    }

    [Fact]
    public void Build_IgnoresAnInvalidSelectedStreamIndexInsteadOfFailingTheFrame()
    {
        var source = Source(Batch(0, [1e-6, 2e-6])) with
        {
            Channels = [new LiveDisplayChannel(7, 7, "invalid", "Reference", true)],
        };

        var frame = WaveformDisplayFrameBuilder.Build(source, displayWindowSeconds: 1, horizontalPixels: 500);

        Assert.Empty(Assert.Single(Assert.IsType<WaveformDisplayFrame>(frame).Traces).Points);
    }

    [Fact]
    public void Build_KeepsPageAlignmentAfterTheRingBufferEvictsTheSessionStart()
    {
        var source = Source(Batch(150, Enumerable.Repeat(1e-6, 70).ToArray())) with
        {
            SessionFirstSampleCounter = 0,
        };

        var frame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(
            source,
            displayWindowSeconds: 10,
            horizontalPixels: 500));

        Assert.Equal(200, frame.WindowStartSampleCounter);
        Assert.Equal(2, frame.CursorSeconds, precision: 10);
        Assert.Equal(200, Assert.Single(frame.Traces).Points[0].SampleCounter);
    }

    [Fact]
    public void Build_DecimatesAcrossSmallVendorBatchBoundariesWithChronologicalExtrema()
    {
        var batches = Enumerable.Range(0, 100)
            .Select(batchIndex => Batch(
                batchIndex * 10L,
                Enumerable.Range(batchIndex * 10, 10)
                    .Select(sample => sample * 1e-9)
                    .ToArray()))
            .ToArray();

        var frame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(
            Source(batches),
            displayWindowSeconds: 100,
            horizontalPixels: 10));

        var points = Assert.Single(frame.Traces).Points;
        Assert.InRange(points.Count, 10, 40);
        Assert.Equal(0, points[0].DisplaySampleOffset);
        Assert.Equal(999, points[^1].DisplaySampleOffset);
        Assert.True(points[0].StartsSegment);
        Assert.All(points.Skip(1), point => Assert.False(point.StartsSegment));
        Assert.All(points, point => Assert.Equal(point.MinVolts, point.MaxVolts));
        Assert.Contains(points, point => point.SampleCounter == 99 && point.MaxVolts == 99e-9);
        Assert.Contains(points, point => point.SampleCounter == 999 && point.MaxVolts == 999e-9);
    }

    [Fact]
    public void Build_DoesNotPlaceBucketExtremaAtTheSameHorizontalPosition()
    {
        var frame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(
            Source(Batch(0, [0d, 4d, -3d, 2d, 1d, -2d, 5d, 0d])),
            displayWindowSeconds: 1,
            horizontalPixels: 1));

        var points = Assert.Single(frame.Traces).Points;

        Assert.Equal(points.Count, points.Select(point => point.DisplaySampleOffset).Distinct().Count());
        Assert.Equal([0L, 2L, 6L, 7L], points.Select(point => point.SampleCounter));
        Assert.All(points, point => Assert.Equal(point.MinVolts, point.MaxVolts));
    }

    [Fact]
    public void Build_StartsANewTraceSegmentAtAFilterConfigurationBoundary()
    {
        var source = Source(Batch(100, [1e-6, 2e-6, 3e-6, 4e-6])) with
        {
            DisplayFilterBoundaries = [102],
        };

        var frame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(
            source,
            displayWindowSeconds: 1,
            horizontalPixels: 500));

        var points = Assert.Single(frame.Traces).Points;
        Assert.Equal([100L, 101L, 102L, 103L], points.Select(point => point.SampleCounter));
        Assert.True(points[0].StartsSegment);
        Assert.False(points[1].StartsSegment);
        Assert.True(points[2].StartsSegment);
        Assert.False(points[3].StartsSegment);
    }

    [Fact]
    public void Build_DerivesChannelReferenceFromRawSamplesWithoutChangingTheBatch()
    {
        var snapshot = new ChannelConfigurationProfile(
            "channels", "通道", "", ChannelConfigurationSource.User, "test-signature",
            [
                new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "F3", true, 0),
                new ChannelConfigurationEntry(1, AcquisitionChannelKind.Reference, "F4", true, 1),
            ], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var montage = new MontageProfile(
            "montage", "F3-F4", "", MontageProfileSource.User, "test-signature",
            ChannelConfigurationFingerprint.Create(snapshot), snapshot,
            [new DerivedMontageChannel("F3-F4", "F3", MontageNegativeKind.Channel, ["F4"], 0)],
            1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            MontageProfileFingerprint.Create(
                ChannelConfigurationFingerprint.Create(snapshot),
                [new DerivedMontageChannel("F3-F4", "F3", MontageNegativeKind.Channel, ["F4"], 0)]));
        var batch = new AcquisitionBatch(0, 2, 2, [5e-6, 2e-6, 7e-6, 3e-6], DateTimeOffset.UtcNow);
        var source = new LiveWaveformSource(
            new AcquisitionStreamMetadata(
                "test-device", "test device", 2,
                [
                    new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
                    new AcquisitionChannel(1, 1, "F4", AcquisitionChannelKind.Reference, "V"),
                ], 1, DateTimeOffset.UtcNow),
            [batch],
            [
                new LiveDisplayChannel(0, 0, "F3", "Reference", true),
                new LiveDisplayChannel(1, 1, "F4", "Reference", true),
            ],
            MontageProfile: montage,
            MontageSourceChannels:
            [
                new LiveDisplayChannel(0, 0, "F3", "Reference", true),
                new LiveDisplayChannel(1, 1, "F4", "Reference", true),
            ]);

        var frame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(source, 1, 500));

        var trace = Assert.Single(frame.Traces);
        Assert.Equal("F3-F4", trace.Label);
        Assert.Equal(3e-6, trace.Points[0].MinVolts, precision: 12);
        Assert.Equal(4e-6, trace.Points[^1].MaxVolts, precision: 12);
        Assert.Equal([5e-6, 2e-6, 7e-6, 3e-6], batch.SampleMajorValues);
    }

    private static LiveWaveformSource Source(params AcquisitionBatch[] batches) =>
        new(
            new AcquisitionStreamMetadata(
                "test-device",
                "test device",
                10,
                [
                    new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
                    new AcquisitionChannel(1, 29, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
                ],
                1,
                DateTimeOffset.UtcNow),
            batches,
            [new LiveDisplayChannel(0, 0, "F3", "Reference", true)]);

    private static AcquisitionBatch Batch(long firstCounter, double[] values) =>
        new(firstCounter, values.Length, 1, values, DateTimeOffset.UtcNow);
}
