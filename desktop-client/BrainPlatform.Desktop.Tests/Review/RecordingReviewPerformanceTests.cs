using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RecordingReviewPerformanceTests
{
    [Fact]
    public async Task FourKilohertzThirtyColumnWindowIsBoundedToRequestedSamples()
    {
        var directory = Path.Combine(Path.GetTempPath(), "brain-platform-review-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var channels = Enumerable.Range(0, 28)
                .Select(index => new AcquisitionChannel(index, index, $"E{index + 1}", AcquisitionChannelKind.Reference, "V"))
                .Append(new AcquisitionChannel(28, 28, "Trigger", AcquisitionChannelKind.Trigger, "code"))
                .Append(new AcquisitionChannel(29, 29, "Counter", AcquisitionChannelKind.SampleCounter, "count"))
                .ToArray();
            var manifest = new
            {
                SessionId = Guid.NewGuid(),
                PayloadFormat = "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
                EegSignalUnit = "V",
                DeviceId = "ant-eego",
                DeviceName = "EE-511",
                SamplingRateHz = 4000,
                SampleCounterChannelIndex = 29,
                RecordingStartUtc = DateTimeOffset.UtcNow,
                Channels = channels,
                Project = new AcquisitionProjectContext("project", "P001", "性能测试", directory, "{}"),
                HardwareConfiguration = new Dictionary<string, string>(),
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest));
            using (var stream = File.Create(Path.Combine(directory, "samples-000001.bin")))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0L);
                writer.Write(4000);
                writer.Write(30);
                writer.Write(DateTimeOffset.UtcNow.UtcTicks);
                for (var sample = 0; sample < 4000; sample++)
                {
                    for (var channel = 0; channel < 30; channel++)
                    {
                        writer.Write(channel == 29 ? (double)sample : channel * 1e-6);
                    }
                }
            }

            await using var recording = await LocalRawRecordingReader.OpenAsync(directory, CancellationToken.None);
            Assert.Equal(4000, recording.Manifest.SamplingRateHz);
            Assert.Equal(30, recording.Index.Batches[0].ChannelCount);
            Assert.Equal(0, recording.Index.PayloadBytesReadDuringOpen);

            var window = await recording.Reader.ReadWindowAsync(0.25, 0.5, CancellationToken.None);

            var segment = Assert.Single(window.Segments);
            Assert.Equal(2000, segment.SampleCount);
            Assert.Equal(2000 * 30, segment.SampleMajorValues.Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
