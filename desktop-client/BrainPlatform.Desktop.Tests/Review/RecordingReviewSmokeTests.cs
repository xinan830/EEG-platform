using System.Text.Json;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.Review;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RecordingReviewSmokeTests
{
    [Fact]
    public async Task OpensProjectOwnedRecordingAndPublishesInitialFrame()
    {
        var directory = Path.Combine(Path.GetTempPath(), "brain-platform-review-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = ChannelConfiguration();
            var acquisition = Profile(configuration);
            var sessionId = Guid.NewGuid();
            var manifest = new
            {
                SessionId = sessionId,
                PayloadFormat = "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
                EegSignalUnit = "V",
                DeviceId = "ant-eego",
                DeviceName = "EE-511",
                SamplingRateHz = 10,
                SampleCounterChannelIndex = 1,
                RecordingStartUtc = DateTimeOffset.UtcNow,
                Channels = new[]
                {
                    new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
                    new AcquisitionChannel(1, 1, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
                },
                Project = new AcquisitionProjectContext("project", "P001", "测试项目", directory, "{}"),
                HardwareConfiguration = new Dictionary<string, string>
                {
                    ["montage_configuration_snapshot_json"] = JsonSerializer.Serialize(acquisition),
                },
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest));
            using (var stream = File.Create(Path.Combine(directory, "samples-000001.bin")))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0L);
                writer.Write(2);
                writer.Write(2);
                writer.Write(DateTimeOffset.UtcNow.UtcTicks);
                writer.Write(1e-6);
                writer.Write(0d);
                writer.Write(2e-6);
                writer.Write(1d);
            }

            await using var recording = await LocalRawRecordingReader.OpenAsync(directory, CancellationToken.None);
            var catalog = RecordingMontageCatalog.Build(recording.Manifest, [acquisition]);
            await using var viewModel = new RecordingReviewViewModel(
                recording.Reader,
                catalog,
                recordingName: "测试记录",
                projectName: recording.Manifest.Project.Name);

            await viewModel.InitializeAsync();

            Assert.Equal(sessionId, viewModel.CurrentFrame?.RecordingSessionId);
            Assert.Equal("测试项目", viewModel.ProjectName);
            Assert.Equal("测试记录", viewModel.RecordingName);
            Assert.Equal(1, viewModel.OutputChannelCount);
            Assert.Equal("已加载", viewModel.StatusText);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ChannelConfigurationProfile ChannelConfiguration() => new(
        "channel-a",
        "ANT默认通道配置",
        "",
        ChannelConfigurationSource.System,
        "ant-eego",
        [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "F3", true, 0)],
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);

    private static MontageProfile Profile(ChannelConfigurationProfile configuration)
    {
        var row = new DerivedMontageChannel("F3-REF", "F3", MontageNegativeKind.OriginalHardwareReference, [], 0);
        var fingerprint = ChannelConfigurationFingerprint.Create(configuration);
        return new MontageProfile(
            "montage-a",
            "ANT默认通道配置 REF参考",
            "",
            MontageProfileSource.System,
            "ant-eego",
            fingerprint,
            configuration,
            [row],
            1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            MontageProfileFingerprint.Create(fingerprint, [row]));
    }
}
