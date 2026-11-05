using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Events.Domain;
using BrainPlatform.Desktop.Modules.Recordings.Access;
using BrainPlatform.Desktop.Modules.Review.Export;
using BrainPlatform.Desktop.Modules.Review.Playback;
using System.Text.Json;
using SharpEDFlib;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RecordingReviewSignalExporterTests
{
    [Fact]
    public async Task Export_the_provided_recording_when_acceptance_paths_are_configured()
    {
        var recordingDirectory = Environment.GetEnvironmentVariable("BRAIN_PLATFORM_EXPORT_RECORDING");
        var outputDirectory = Environment.GetEnvironmentVariable("BRAIN_PLATFORM_EXPORT_OUTPUT");
        if (string.IsNullOrWhiteSpace(recordingDirectory) || string.IsNullOrWhiteSpace(outputDirectory))
            throw Xunit.Sdk.SkipException.ForSkip("Set BRAIN_PLATFORM_EXPORT_RECORDING and BRAIN_PLATFORM_EXPORT_OUTPUT to run the local recording acceptance test.");

        var manifestJson = await File.ReadAllTextAsync(Path.Combine(recordingDirectory, "manifest.json"));
        using var manifestDocument = JsonDocument.Parse(manifestJson);
        var recordingId = manifestDocument.RootElement.GetProperty("SessionId").GetGuid();
        Directory.CreateDirectory(outputDirectory);

        await using var recording = await LocalRawRecordingReader.OpenAsync(
            recordingDirectory, recordingId, CancellationToken.None);
        var eegChannels = recording.Manifest.Channels
            .Where(channel => channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
            .OrderBy(channel => channel.StreamIndex)
            .ToArray();
        var sampleCount = checked((int)(recording.LastSampleCounter - recording.FirstSampleCounter + 1));
        Assert.InRange(Math.Abs(149.982 - sampleCount / (double)recording.Manifest.SamplingRateHz), 0, 0.0001);
        var range = new RecordingSampleRange(recording.FirstSampleCounter,
            checked(recording.FirstSampleCounter + sampleCount));
        var data = await recording.ReadSamplesAsync(range,
            eegChannels.Select(channel => channel.StreamIndex).ToArray(), CancellationToken.None);

        foreach (var (format, extension) in new[]
                 {
                     (EDFileType.EDFPlus, ".edf"),
                     (EDFileType.BDFPlus, ".bdf"),
                 })
        {
            var path = Path.Combine(outputDirectory, "recording-review-acceptance" + extension);
            await RecordingReviewSignalExporter.ExportAsync(path, format, recording.Manifest,
                recording.Manifest.RecordingStartUtc, recording.FirstSampleCounter, 0,
                data, [], CancellationToken.None);

            using var reader = new EDFReader();
            reader.Open(path, EDFReadFlag.DoNotReadAnnotations);
            Assert.Equal(format, reader.Header.FileType);
            Assert.Equal(eegChannels.Length, reader.Header.SignalCount);
            Assert.Equal(sampleCount / reader.Header.Signals.SampleCountPerDataRecords[0], reader.Header.DataRecordCount);
            Assert.Equal(sampleCount, reader.Header.Signals.SampleCountPerDataRecords[0] * reader.Header.DataRecordCount);
            Assert.InRange(Math.Abs(sampleCount / (double)recording.Manifest.SamplingRateHz -
                reader.Header.DataRecordCount * reader.Header.DataRecordDurationInSeconds), 0, 0.0001);
            Assert.InRange(Math.Abs(recording.Manifest.SamplingRateHz -
                reader.Header.Signals.SampleCountPerDataRecords[0] / reader.Header.DataRecordDurationInSeconds), 0, 1e-9);
            Assert.All(reader.Header.Signals.Labels, label => Assert.False(string.IsNullOrWhiteSpace(label)));

            using var mappings = JsonDocument.Parse(await File.ReadAllTextAsync(path + ".labels.json"));
            var root = mappings.RootElement;
            Assert.Equal("recording-review-export-metadata-v1", root.GetProperty("SchemaVersion").GetString());
            Assert.Equal(recording.Manifest.SamplingRateHz, root.GetProperty("SamplingRateHz").GetInt32());
            Assert.Equal(28, root.GetProperty("ChannelMappings").GetArrayLength());
            Assert.True(root.GetProperty("HardwareConfiguration").TryGetProperty("channel_configuration_snapshot_json", out _));
            Assert.False(root.GetProperty("HardwareConfiguration").TryGetProperty("serial_number", out _));
            Assert.Equal("Bipolar", root.GetProperty("ChannelMappings")[24].GetProperty("Kind").GetString());
            var labelMappings = root.GetProperty("ChannelMappings");
            Assert.Equal(eegChannels.Count(channel => string.IsNullOrWhiteSpace(channel.Label)),
                labelMappings.EnumerateArray().Count(item => item.GetProperty("SourceLabel").ValueKind == JsonValueKind.Null));
            Assert.All(labelMappings.EnumerateArray().Where(item => item.GetProperty("SourceLabel").ValueKind == JsonValueKind.Null),
                item => Assert.StartsWith("CH", item.GetProperty("ExportLabel").GetString(), StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Review_toolbar_export_button_is_bound_to_the_edf_bdf_action()
    {
        var xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RecordingReviewView.xaml"));
        Assert.Contains("Click=\"OnExportClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("EDF/BDF", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EDFileType.EDFPlus, ".edf")]
    [InlineData(EDFileType.BDFPlus, ".bdf")]
    public async Task Export_round_trips_source_samples_and_annotations(EDFileType format, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"review-export-{Guid.NewGuid():N}{extension}");
        var fixture = CreateFixture();
        try
        {
            await RecordingReviewSignalExporter.ExportAsync(path, format, fixture.Manifest,
                fixture.StartUtc, fixture.FirstCounter, 2, fixture.Data, fixture.Events, CancellationToken.None);

            using var reader = new EDFReader();
            reader.Open(path, EDFReadFlag.ReadAnnotations);
            Assert.Equal(format, reader.Header.FileType);
            Assert.Equal(["CH0001", "CH0002"], reader.Header.Signals.Labels);
            Assert.Equal(2, reader.Header.SignalCount);
            Assert.Equal(2, reader.Header.Signals.SampleCountPerDataRecords[0]);
            Assert.Equal(fixture.StartUtc.AddSeconds(1).UtcDateTime, reader.Header.RecordStartDateTime);
            var actual = new double[2, 2];
            Assert.Equal(2, reader.ReadPhysicalSamples(actual));
            Assert.InRange(Math.Abs(actual[0, 0] - 1), 0, format == EDFileType.EDFPlus ? 0.001 : 0.00001);
            Assert.InRange(Math.Abs(actual[1, 1] + 4), 0, format == EDFileType.EDFPlus ? 0.001 : 0.00001);
            Assert.Equal(1, reader.GetAnnotationCount());
            var annotations = Enumerable.Range(0, reader.GetAnnotationCount())
                .Select(reader.ReadAnnotation).ToArray();
            var annotation = Assert.Single(annotations, item => item.Annotation == "eyes closed");
            Assert.Equal("eyes closed", annotation.Annotation);
            Assert.Equal(500_000, annotation.OnsetInMicroseconds);
            Assert.Equal(500_000, annotation.DurationInMicroseconds);
            using var labelMap = JsonDocument.Parse(File.ReadAllText(path + ".labels.json"));
            var mappings = labelMap.RootElement.GetProperty("ChannelMappings");
            Assert.Equal("CH0001", mappings[0].GetProperty("ExportLabel").GetString());
            Assert.Equal("EEG-Fp1-Long-Source-Label", mappings[0].GetProperty("SourceLabel").GetString());
            Assert.Equal(JsonValueKind.Null, mappings[1].GetProperty("SourceLabel").ValueKind);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".labels.json");
        }
    }

    [Fact]
    public async Task Export_rejects_gaps_and_unknown_units_without_leaving_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"review-export-{Guid.NewGuid():N}.edf");
        var fixture = CreateFixture();
        var gapped = fixture.Data with
        {
            Window = fixture.Data.Window with
            {
                Segments =
                [
                    fixture.Data.Window.Segments[0] with { SampleCount = 1, SampleMajorValues = [0.000001, -0.000002] },
                    new RecordingReviewSegment(1002, 1, 2, [0.000003, -0.000004]),
                ],
            },
        };
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => RecordingReviewSignalExporter.ExportAsync(
                path, EDFileType.EDFPlus, fixture.Manifest, fixture.StartUtc, fixture.FirstCounter, 2,
                gapped, fixture.Events, CancellationToken.None));
            Assert.False(File.Exists(path));

            var unknownUnit = fixture.Manifest with
            {
                Channels = fixture.Manifest.Channels.Select(channel =>
                    channel.StreamIndex == 0 ? channel with { Unit = "mV" } : channel).ToArray(),
            };
            await Assert.ThrowsAsync<InvalidDataException>(() => RecordingReviewSignalExporter.ExportAsync(
                path, EDFileType.EDFPlus, unknownUnit, fixture.StartUtc, fixture.FirstCounter, 2,
                fixture.Data, fixture.Events, CancellationToken.None));
            Assert.False(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ExportFixture CreateFixture()
    {
        var firstCounter = 1000L;
        var range = new RecordingSampleRange(firstCounter + 2, firstCounter + 4);
        var channels = new[]
        {
            new AcquisitionChannel(0, 0, "EEG-Fp1-Long-Source-Label", AcquisitionChannelKind.Reference, "V"),
            new AcquisitionChannel(2, 2, null, AcquisitionChannelKind.Bipolar, "V"),
        };
        var manifestChannels =
        new[]
        {
            channels[0],
            new AcquisitionChannel(1, 1, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
            channels[1],
        };
        var startUtc = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero).AddMilliseconds(500);
        var manifest = new LocalRawRecordingManifest(Guid.NewGuid(), "sample_major_float64_v1", "V", "test-device",
            "Test EEG", 2, 1, startUtc,
            manifestChannels,
            new AcquisitionProjectContext("p1", "P-1", "Test", Path.GetTempPath(), "{}"),
            new Dictionary<string, string>());
        var segment = new RecordingReviewSegment(firstCounter + 2, 2, 2,
            [0.000001, -0.000002, 0.000003, -0.000004]);
        var data = new RecordingAccessReadResult(range, range, channels,
            new RecordingReviewWindow(1, 1, 2, [segment]));
        var events = new[]
        {
            new RecordingEvent("in", manifest.SessionId.ToString("N"), "eyes", new("eyes", "eyes closed", "#000000", 1),
                EventSource.ManualButton, null, null, 3, 1, startUtc, startUtc),
            new RecordingEvent("out", manifest.SessionId.ToString("N"), "other", new("other", "outside", "#000000", 1),
                EventSource.ManualButton, null, null, 4, 0, startUtc, startUtc),
        };
        return new ExportFixture(manifest, data, events, startUtc, firstCounter);
    }

    private sealed record ExportFixture(
        LocalRawRecordingManifest Manifest,
        RecordingAccessReadResult Data,
        IReadOnlyList<RecordingEvent> Events,
        DateTimeOffset StartUtc,
        long FirstCounter);
}
