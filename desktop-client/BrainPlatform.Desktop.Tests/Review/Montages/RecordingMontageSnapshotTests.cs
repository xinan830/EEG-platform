using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RecordingMontageSnapshotTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Minimal_imported_snapshot_supports_display_navigation_and_cached_redisplay(bool catalogueOnly)
    {
        var manifest = Manifest(MinimalSnapshot(), catalogueOnly);
        var originalJson = manifest.HardwareConfiguration.ToDictionary(pair => pair.Key, pair => pair.Value);
        var catalog = RecordingMontageCatalog.Build(manifest, []);
        var profile = Assert.Single(catalog.CompatibleViewingMontages).Profile;
        Assert.Equal(ChannelConfigurationFingerprint.Create(Configuration()), profile.ChannelConfigurationFingerprint);
        Assert.Equal(MontageProfileFingerprint.Create(profile.ChannelConfigurationFingerprint,
            profile.DerivedChannels, profile.AverageReferenceLabels), profile.Fingerprint);
        Assert.Empty(catalog.Incompatible);
        var identity = ReviewDisplaySourceIdentity.CreateFiltered(manifest, profile,
            new(1, 30, 50), new("v1", "contract"));
        var reread = Assert.Single(RecordingMontageCatalog.Build(manifest, []).CompatibleViewingMontages).Profile;
        Assert.Equal(identity, ReviewDisplaySourceIdentity.CreateFiltered(manifest, reread,
            new(1, 30, 50), new("v1", "contract")));
        Assert.All(originalJson, pair => Assert.Equal(pair.Value, manifest.HardwareConfiguration[pair.Key]));

        await using var reader = new Reader(manifest);
        var selection = new WorkspaceMontageSelection<MontageProfile>(manifest.SessionId.ToString("N"),
            profile, [profile]);
        await using var session = new RecordingReviewSession(reader, catalog, selection, 5);
        await session.InitializeAsync();
        var original = Assert.IsType<RecordingReviewFrame>(session.CurrentFrame);
        Assert.Equal("已加载", session.StatusText);
        Assert.Equal(["F3-AVG", "O1-AVG"], original.OutputChannelNames);
        Assert.All(original.Segments, segment =>
        {
            Assert.All(segment.Channels[0].Values, value => Assert.Equal(-1e-6, value, 12));
            Assert.All(segment.Channels[1].Values, value => Assert.Equal(1e-6, value, 12));
        });
        Assert.True(await session.NavigateFromTrackClickAsync(30));
        Assert.Equal(30, session.PositionSeconds);
        var reads = reader.Reads;
        Assert.True(await session.NavigateFromTrackClickAsync(0));
        Assert.Same(original, session.CurrentFrame);
        Assert.Equal(reads, reader.Reads);
    }

    [Fact]
    public void Explicit_saved_fingerprints_are_preserved()
    {
        var snapshot = MinimalSnapshot();
        snapshot["ChannelConfigurationFingerprint"] = "saved-channel-identity";
        snapshot["Fingerprint"] = "saved-montage-identity";
        var profile = RecordingMontageCatalog.Build(Manifest(snapshot), []).AcquisitionMontage!;
        Assert.Equal("saved-channel-identity", profile.ChannelConfigurationFingerprint);
        Assert.Equal("saved-montage-identity", profile.Fingerprint);
    }

    [Fact]
    public void Changed_formulas_change_display_identity()
    {
        var average = MinimalSnapshot();
        var originalReference = MinimalSnapshot();
        originalReference["AverageReferenceLabels"] = new JsonArray();
        foreach (var channel in originalReference["DerivedChannels"]!.AsArray())
        {
            channel!["Name"] = channel["PositiveLabel"]!.GetValue<string>() + "-REF";
            channel["NegativeKind"] = 0;
            channel["NegativeLabels"] = new JsonArray();
        }
        var manifest = Manifest(average);
        var a = RecordingMontageCatalog.Build(manifest, []).AcquisitionMontage!;
        var b = RecordingMontageCatalog.Build(Manifest(originalReference), []).AcquisitionMontage!;
        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
        Assert.NotEqual(ReviewDisplaySourceIdentity.CreateRaw(manifest, a),
            ReviewDisplaySourceIdentity.CreateRaw(manifest, b));
    }

    [Theory]
    [InlineData("channel-snapshot")]
    [InlineData("derived-null")]
    [InlineData("derived-entry-null")]
    [InlineData("negative-null")]
    [InlineData("negative-entry-null")]
    [InlineData("positive-null")]
    [InlineData("name-null")]
    [InlineData("device-null")]
    [InlineData("average-null-entry")]
    [InlineData("snapshot-label-null")]
    public void Malformed_snapshot_is_rejected_before_catalogue_deduplication(string defect)
    {
        var snapshot = MinimalSnapshot();
        var first = snapshot["DerivedChannels"]![0]!;
        switch (defect)
        {
            case "channel-snapshot": snapshot.Remove("ChannelConfigurationSnapshot"); break;
            case "derived-null": snapshot["DerivedChannels"] = null; break;
            case "derived-entry-null": snapshot["DerivedChannels"]![0] = null; break;
            case "negative-null": first["NegativeLabels"] = null; break;
            case "negative-entry-null": first["NegativeLabels"] = new JsonArray((JsonNode?)null); break;
            case "positive-null": first["PositiveLabel"] = null; break;
            case "name-null": first["Name"] = null; break;
            case "device-null": snapshot["DeviceSignature"] = null; break;
            case "average-null-entry": snapshot["AverageReferenceLabels"] = new JsonArray((JsonNode?)null); break;
            case "snapshot-label-null": snapshot["ChannelConfigurationSnapshot"]!["Channels"]![0]!["ElectrodeLabel"] = null; break;
        }
        foreach (var catalogueOnly in new[] { false, true })
        {
            var catalog = RecordingMontageCatalog.Build(Manifest(snapshot, catalogueOnly), []);
            Assert.Null(catalog.AcquisitionMontage);
            Assert.Empty(catalog.CompatibleViewingMontages);
            Assert.NotEmpty(catalog.Incompatible);
            Assert.All(catalog.Incompatible, item => Assert.False(string.IsNullOrWhiteSpace(item.Reason)));
            if (!catalogueOnly) Assert.Equal(AcquisitionMontageStatus.InvalidSnapshot, catalog.AcquisitionMontageStatus);
        }
    }

    [Fact]
    public void Malformed_catalogue_entry_does_not_hide_valid_imported_montage()
    {
        var manifest = Manifest(MinimalSnapshot(), true);
        var hardware = manifest.HardwareConfiguration.ToDictionary(pair => pair.Key, pair => pair.Value);
        hardware["montage_catalogue_snapshot_json"] = new JsonArray(null, MinimalSnapshot()).ToJsonString();
        var catalog = RecordingMontageCatalog.Build(manifest with { HardwareConfiguration = hardware }, []);
        Assert.Single(catalog.CompatibleViewingMontages);
        Assert.Single(catalog.Incompatible);
    }

    private static ChannelConfigurationProfile Configuration() => new(
        "dataset-layout", "Source EEG", "", ChannelConfigurationSource.User, "dataset",
        [new(0, AcquisitionChannelKind.Reference, "F3", true, 0),
         new(1, AcquisitionChannelKind.Reference, "O1", true, 1)],
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static JsonObject MinimalSnapshot() => new()
    {
        ["Id"] = "dataset-car", ["Name"] = "Dataset CAR", ["DeviceSignature"] = "dataset",
        ["ChannelConfigurationSnapshot"] = JsonSerializer.SerializeToNode(Configuration()),
        ["AverageReferenceLabels"] = new JsonArray("F3", "O1"),
        ["DerivedChannels"] = JsonSerializer.SerializeToNode(new DerivedMontageChannel[]
        {
            new("F3-AVG", "F3", MontageNegativeKind.Mean, ["F3", "O1"], 0),
            new("O1-AVG", "O1", MontageNegativeKind.Mean, ["F3", "O1"], 1),
        }),
    };

    private static LocalRawRecordingManifest Manifest(JsonObject snapshot, bool catalogueOnly = false)
    {
        var hardware = new Dictionary<string, string>
            { ["channel_configuration_snapshot_json"] = JsonSerializer.Serialize(Configuration()) };
        hardware[catalogueOnly ? "montage_catalogue_snapshot_json" : "montage_configuration_snapshot_json"] =
            catalogueOnly ? new JsonArray(snapshot.DeepClone()).ToJsonString() : snapshot.ToJsonString();
        return new(Guid.NewGuid(), "edf-bdf", "V", "imported", ".bdf", 250, -1, DateTimeOffset.UnixEpoch,
            [new(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
             new(1, 1, "O1", AcquisitionChannelKind.Reference, "V")],
            new AcquisitionProjectContext("p", "", "fixture", "", "{}"), hardware);
    }

    private sealed class Reader(LocalRawRecordingManifest manifest) : IRecordingAccessSession
    {
        public int Reads { get; private set; }
        public LocalRawRecordingManifest Manifest => manifest;
        public double DurationSeconds => 60;
        public long FirstSampleCounter => 0;
        public Task<RecordingAccessReadResult> ReadSamplesAsync(RecordingSampleRange range,
            IReadOnlyList<int>? channelIndexes, CancellationToken cancellationToken)
        {
            Reads++;
            var count = checked((int)(range.EndSampleExclusive - range.StartSample));
            var values = Enumerable.Range(0, count).SelectMany(_ => new[] { 1e-6, 3e-6 }).ToArray();
            var window = new RecordingReviewWindow(range.StartSample / 250d, range.StartSample / 250d,
                range.EndSampleExclusive / 250d, [new(range.StartSample, count, 2, values)]);
            return Task.FromResult(new RecordingAccessReadResult(range, range, manifest.Channels, window));
        }
    }
}
