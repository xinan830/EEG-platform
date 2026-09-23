using BrainPlatform.Desktop.Modules.Acquisition.Waveform;

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

    [Fact]
    public void Build_DerivesSharedAverageReferenceWithoutChangingTheRawBatch()
    {
        var channelSnapshot = new ChannelConfigurationProfile(
            "channels", "通道", "", ChannelConfigurationSource.User, "test-signature",
            [
                new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "F3", true, 0),
                new ChannelConfigurationEntry(1, AcquisitionChannelKind.Reference, "F4", true, 1),
                new ChannelConfigurationEntry(2, AcquisitionChannelKind.Reference, "Cz", true, 2),
            ], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var derivedChannels = new DerivedMontageChannel[]
        {
            new("F3-AVG", "F3", MontageNegativeKind.Mean, ["F3", "F4", "Cz"], 0),
            new("F4-AVG", "F4", MontageNegativeKind.Mean, ["F3", "F4", "Cz"], 1),
        };
        var channelFingerprint = ChannelConfigurationFingerprint.Create(channelSnapshot);
        var montage = new MontageProfile(
            "montage", "平均参考", "", MontageProfileSource.User, "test-signature",
            channelFingerprint, channelSnapshot, derivedChannels,
            1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            MontageProfileFingerprint.Create(channelFingerprint, derivedChannels));
        var rawValues = new[] { 5d, 2d, 3d, 7d, 3d, 5d };
        var batch = new AcquisitionBatch(0, 2, 3, rawValues.ToArray(), DateTimeOffset.UtcNow);
        var sourceChannels = new LiveDisplayChannel[]
        {
            new(0, 0, "F3", "Reference", true),
            new(1, 1, "F4", "Reference", true),
            new(2, 2, "Cz", "Reference", true),
        };
        var source = new LiveWaveformSource(
            new AcquisitionStreamMetadata(
                "test-device", "test device", 1,
                [
                    new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
                    new AcquisitionChannel(1, 1, "F4", AcquisitionChannelKind.Reference, "V"),
                    new AcquisitionChannel(2, 2, "Cz", AcquisitionChannelKind.Reference, "V"),
                ], 2, DateTimeOffset.UtcNow),
            [batch], sourceChannels,
            MontageProfile: montage,
            MontageSourceChannels: sourceChannels);

        var frame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(source, 2, 500));

        Assert.Equal(2, frame.Traces.Count);
        Assert.Equal(5d / 3d, frame.Traces[0].Points[0].MinVolts, precision: 12);
        Assert.Equal(2d, frame.Traces[0].Points[^1].MaxVolts, precision: 12);
        Assert.Equal(-4d / 3d, frame.Traces[1].Points[0].MinVolts, precision: 12);
        Assert.Equal(-2d, frame.Traces[1].Points[^1].MaxVolts, precision: 12);
        Assert.Equal(rawValues, batch.SampleMajorValues);
    }

    [Fact]
    public void ReferenceSignalCache_ComputesOneSharedReferencePerSample()
    {
        var batch = new AcquisitionBatch(0, 2, 3, [5d, 2d, 3d, 7d, 3d, 5d], DateTimeOffset.UtcNow);
        var first = new DerivedMontageChannel(
            "F3-AVG", "F3", MontageNegativeKind.Mean, ["F3", "F4", "Cz"], 0);
        var second = new DerivedMontageChannel(
            "F4-AVG", "F4", MontageNegativeKind.Mean, ["F3", "F4", "Cz"], 1);
        var channels = new ResolvedMontageChannel[]
        {
            new(first, 0, [0, 1, 2], "0,1,2"),
            new(second, 1, [0, 1, 2], "0,1,2"),
        };

        var cache = ReferenceSignalCache.Create([batch], channels);

        Assert.Equal(batch.SampleCount, cache.ComputedReferenceSampleCount);
        Assert.Equal(10d / 3d, cache.Read("0,1,2", batch, 0), precision: 12);
        Assert.Equal(5d, cache.Read("0,1,2", batch, 1), precision: 12);
    }

    [Fact]
    public void Build_DecimatesTenSecondsOfFourKilohertzAverageReferenceToScreenDensity()
    {
        const int samplingRateHz = 4_000;
        const int eegChannelCount = 21;
        const int samplesPerBatch = 200;
        var labels = Enumerable.Range(1, eegChannelCount).Select(index => $"EEG{index:00}").ToArray();
        var channelEntries = labels.Select((label, index) =>
            new ChannelConfigurationEntry(index, AcquisitionChannelKind.Reference, label, true, index)).ToArray();
        var channelSnapshot = new ChannelConfigurationProfile(
            "channels", "通道", "", ChannelConfigurationSource.User, "test-signature",
            channelEntries, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var derivedChannels = labels.Select((label, index) =>
            new DerivedMontageChannel($"{label}-AVG", label, MontageNegativeKind.Mean, labels, index)).ToArray();
        var channelFingerprint = ChannelConfigurationFingerprint.Create(channelSnapshot);
        var montage = new MontageProfile(
            "montage", "平均参考", "", MontageProfileSource.User, "test-signature",
            channelFingerprint, channelSnapshot, derivedChannels,
            1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            MontageProfileFingerprint.Create(channelFingerprint, derivedChannels));
        var streamChannels = labels.Select((label, index) =>
                new AcquisitionChannel(index, index, label, AcquisitionChannelKind.Reference, "V"))
            .Append(new AcquisitionChannel(
                eegChannelCount,
                eegChannelCount,
                "Counter",
                AcquisitionChannelKind.SampleCounter,
                "count"))
            .ToArray();
        var displayChannels = labels.Select((label, index) =>
            new LiveDisplayChannel(index, index, label, "Reference", true)).ToArray();
        var batches = Enumerable.Range(0, samplingRateHz * 10 / samplesPerBatch)
            .Select(batchIndex =>
            {
                var firstCounter = (long)batchIndex * samplesPerBatch;
                var values = new double[samplesPerBatch * streamChannels.Length];
                for (var sample = 0; sample < samplesPerBatch; sample++)
                {
                    var offset = sample * streamChannels.Length;
                    for (var channel = 0; channel < eegChannelCount; channel++)
                    {
                        values[offset + channel] = ((channel + 1) * 1e-6) + ((firstCounter + sample) % 31 * 1e-9);
                    }
                    values[offset + eegChannelCount] = firstCounter + sample;
                }

                return new AcquisitionBatch(
                    firstCounter,
                    samplesPerBatch,
                    streamChannels.Length,
                    values,
                    DateTimeOffset.UtcNow);
            })
            .ToArray();
        var source = new LiveWaveformSource(
            new AcquisitionStreamMetadata(
                "test-device", "test device", samplingRateHz,
                streamChannels, eegChannelCount, DateTimeOffset.UtcNow),
            batches,
            displayChannels,
            MontageProfile: montage,
            MontageSourceChannels: displayChannels);

        var frame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(source, 10, 1_000));

        Assert.Equal(eegChannelCount, frame.Traces.Count);
        Assert.All(frame.Traces, trace => Assert.InRange(trace.Points.Count, 1, 4_000));
        Assert.All(frame.Traces.SelectMany(trace => trace.Points), point => Assert.True(double.IsFinite(point.MinVolts)));
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
