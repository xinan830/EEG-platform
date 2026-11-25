using System.Text.Json;
using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Acquisition.Runtime;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class LiveSpectralPresentationTests
{
    [Fact]
    public void AnalysisChannelNames_ExcludesUnnamedAndNonEegChannels()
    {
        var stream = new AcquisitionStreamMetadata("device", "device", 512,
            [
                new(0, 0, " F3 ", AcquisitionChannelKind.Reference, "V"),
                new(1, 1, null, AcquisitionChannelKind.Reference, "V"),
                new(2, 2, "F3-Fp1", AcquisitionChannelKind.Bipolar, "V"),
                new(3, 3, "", AcquisitionChannelKind.Bipolar, "V"),
                new(4, 4, "TRIG", AcquisitionChannelKind.Trigger, "V"),
                new(5, 5, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
            ],
            5,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(["F3", "F3-Fp1"], LiveSpectralPresentation.AnalysisChannelNames(stream));
    }

    [Fact]
    public void AnalysisChannels_UseOrderedMontageOutputs()
    {
        var snapshot = MontageSnapshot();

        Assert.Equal(["F3-REF", "C3-REF"], LiveSpectralPresentation.AnalysisChannelNames(snapshot));
    }

    [Fact]
    public void TopomapConfig_UsesMontageOutputNamesAndElectrodePositions()
    {
        var config = LiveSpectralPresentation.MapConfig(MontageSnapshot(), ["psd.band-mean-density.alpha"]);

        Assert.Equal(["F3-REF", "C3-REF"], config.GetProperty("spectrum").GetProperty("channels")
            .EnumerateArray().Select(channel => channel.GetString()));
        Assert.True(config.GetProperty("positions").TryGetProperty("F3-REF", out _));
    }

    [Fact]
    public void TopomapConfig_IgnoresUnselectedUnnamedHardwareInputs()
    {
        var baseSnapshot = MontageSnapshot();
        var snapshot = baseSnapshot with
        {
            Profile = baseSnapshot.Profile with
            {
                ChannelConfigurationSnapshot = baseSnapshot.Profile.ChannelConfigurationSnapshot with
                {
                    Channels = [
                        ..baseSnapshot.Profile.ChannelConfigurationSnapshot.Channels,
                        new ChannelConfigurationEntry(2, AcquisitionChannelKind.Reference, "", false, 2),
                        new ChannelConfigurationEntry(3, AcquisitionChannelKind.Reference, "", false, 3),
                    ],
                },
            },
        };

        var config = LiveSpectralPresentation.MapConfig(snapshot, ["rbp.relative.alpha"]);

        Assert.True(config.GetProperty("positions").TryGetProperty("F3-REF", out _));
    }

    [Fact]
    public void TopomapConfig_UsesPositiveElectrodePositionForMeanReferenceChannels()
    {
        var baseSnapshot = MontageSnapshot();
        var snapshot = baseSnapshot with
        {
            Profile = baseSnapshot.Profile with
            {
                DerivedChannels =
                [
                    new DerivedMontageChannel("F3-AVG", "F3", MontageNegativeKind.Mean, ["F3", "C3"], 0),
                    new DerivedMontageChannel("C3-AVG", "C3", MontageNegativeKind.Mean, ["F3", "C3"], 1),
                ],
            },
            OrderedChannelNames = ["F3-AVG", "C3-AVG"],
        };

        var config = LiveSpectralPresentation.MapConfig(snapshot, ["rbp.relative.alpha"]);

        Assert.Equal(["F3-AVG", "C3-AVG"], config.GetProperty("spectrum").GetProperty("channels")
            .EnumerateArray().Select(channel => channel.GetString()));
        Assert.True(config.GetProperty("positions").TryGetProperty("F3-AVG", out _));
    }

    [Fact]
    public void TopomapConfig_ReportsDuplicateSelectedElectrodeNames()
    {
        var baseSnapshot = MontageSnapshot();
        var snapshot = baseSnapshot with
        {
            Profile = baseSnapshot.Profile with
            {
                ChannelConfigurationSnapshot = baseSnapshot.Profile.ChannelConfigurationSnapshot with
                {
                    Channels = [
                        ..baseSnapshot.Profile.ChannelConfigurationSnapshot.Channels,
                        new ChannelConfigurationEntry(2, AcquisitionChannelKind.Reference, "F3", true, 2),
                    ],
                },
            },
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            LiveSpectralPresentation.MapConfig(snapshot, ["rbp.relative.alpha"]));

        Assert.Contains("重复电极名", error.Message);
    }

    [Fact]
    public void RbpTopomapConfig_PrefersCurrentMontageWhenAvailable()
    {
        var stream = new AcquisitionStreamMetadata("device", "device", 512,
            [new(0, 0, "F3", AcquisitionChannelKind.Reference, "V")], 1, DateTimeOffset.UnixEpoch);

        var config = LiveSpectralPresentation.RbpTopomapConfig(stream, MontageSnapshot(), ["rbp.relative.alpha"]);

        Assert.Equal(["F3-REF", "C3-REF"], config.GetProperty("spectrum").GetProperty("channels")
            .EnumerateArray().Select(channel => channel.GetString()));
    }

    [Fact]
    public void LiveUpdate_ReadsAbsoluteSampleBoundaryForStaleResultFencing()
    {
        using var document = JsonDocument.Parse("""
            {"type":"live_analysis_update","schema_version":"live-analysis-update-v2",
             "acquisition_session_id":"00000000-0000-0000-0000-000000000001","algorithm_id":"psd",
             "revision":1,"montage_revision":2,"state":"ready","start_sample":3250,"end_sample":5750,
             "start_s":13,"end_s":23,"payload":{}}
            """);

        var update = LiveAnalysisUpdate.Read(document.RootElement, Guid.Parse("00000000-0000-0000-0000-000000000001"), 250);

        Assert.Equal(3250, update.StartSample);
        Assert.Equal(5750, update.EndSample);
        Assert.True(update.IsCurrentMontageResult(MontageSnapshot()));
        Assert.False((update with { MontageRevision = 1 }).IsCurrentMontageResult(MontageSnapshot()));
    }

    [Fact]
    public void PsdUsesReviewUnitsAndRejectsWrongChannel()
    {
        var result = Update("psd", """
            {"schema_version":"live-spectrum-result-v1","unit":"V^2/Hz","channels":["F3"],
             "frequencies_hz":[9,10,11],"values":[[null,1.2e-11,4e-12]]}
            """);
        var preview = new PsdPreviewState { ValueUnit = "µV²/Hz" };
        LiveSpectralPresentation.ApplyPsd(preview, result, "F3");
        Assert.Null(preview.Points[0]);
        Assert.Equal(12d, preview.Points[1]!.Value, 12);
        Assert.Throws<InvalidOperationException>(() => LiveSpectralPresentation.ApplyPsd(preview, result, "F4"));
    }

    [Theory]
    [InlineData("[15,16]", true)]
    [InlineData("[2,3]", false)]
    [InlineData("[15,15]", false)]
    public void StftUsesAcquisitionTimeAndPreservesUnavailableCells(string times, bool valid)
    {
        var result = Update("stft", $$"""
            {"schema_version":"live-spectrum-result-v1","unit":"dB re 1 uV^2/Hz","channels":["F3"],
             "frequencies_hz":[9,10],"times_s":{{times}},"values":[[1,2],[3,null]]}
            """);
        var range = new TimeRange(13, 23);
        if (!valid)
        {
            Assert.Throws<InvalidOperationException>(() => LiveSpectralPresentation.ReadStft(result, "F3", range));
            return;
        }
        var preview = LiveSpectralPresentation.ReadStft(result, "F3", range);
        Assert.Equal(new[] { 15d, 16d }, preview.TimesSeconds);
        Assert.Equal(range, preview.RequestedRange);
        Assert.Null(preview.PowerDb[1][1]);
    }

    [Fact]
    public void MapUsesSessionSnapshotAndExcludesBipolarChannels()
    {
        var profile = new ChannelConfigurationProfile(
            "p", "profile", "", ChannelConfigurationSource.User, "device",
            [
                new(12, AcquisitionChannelKind.Reference, "F3", true, 0, ManualMapPosition: new(-0.3, 0.4)),
                new(14, AcquisitionChannelKind.Reference, "C3", false, 1),
                new(15, AcquisitionChannelKind.Bipolar, "F4", true, 2, ManualMapPosition: new(0.3, 0.4)),
                new(16, AcquisitionChannelKind.Reference, "Fz", false, 3),
            ], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var stream = new AcquisitionStreamMetadata("device", "device", 512,
            [new(0, 12, "F3", AcquisitionChannelKind.Reference, "V"),
             new(1, 14, "C3", AcquisitionChannelKind.Reference, "V"),
             new(2, 15, "F4", AcquisitionChannelKind.Bipolar, "V"),
             new(3, 16, "Fz", AcquisitionChannelKind.Reference, "V"),
             new(4, 17, "Counter", AcquisitionChannelKind.SampleCounter, "count")],
            4, DateTimeOffset.UnixEpoch)
        {
            HardwareConfiguration = new Dictionary<string, string>
            {
                ["channel_configuration_snapshot_json"] = JsonSerializer.Serialize(profile),
            },
        };
        var config = LiveSpectralPresentation.MapConfig(stream, "psd.band-mean-density.alpha");
        var positions = config.GetProperty("positions");
        Assert.Equal(-0.3, positions.GetProperty("F3").GetProperty("x").GetDouble());
        Assert.False(positions.TryGetProperty("C3", out _));
        Assert.False(positions.TryGetProperty("F4", out _));
        Assert.Equal(new[] { "F3" }, config.GetProperty("spectrum").GetProperty("channels")
            .EnumerateArray().Select(channel => channel.GetString()));
        Assert.Equal(new[] { "F3", "F4" }, LiveSpectralPresentation.AnalysisChannelNames(stream));
    }

    private static LiveAnalysisUpdate Update(string algorithm, string payload) =>
        new(Guid.NewGuid(), algorithm, 1, "ready", 512, JsonSerializer.Deserialize<JsonElement>(payload), 13, 23);

    private static LiveMontageSnapshot MontageSnapshot()
    {
        var config = new ChannelConfigurationProfile("channels", "Channels", "", ChannelConfigurationSource.User, "device",
            [new(0, AcquisitionChannelKind.Reference, "F3", true, 0, ManualMapPosition: new(-0.3, 0.4)),
             new(1, AcquisitionChannelKind.Reference, "C3", true, 1, ManualMapPosition: new(0, 0.2))],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var derived = new[]
        {
            new DerivedMontageChannel("C3-REF", "C3", MontageNegativeKind.OriginalHardwareReference, [], 1),
            new DerivedMontageChannel("F3-REF", "F3", MontageNegativeKind.OriginalHardwareReference, [], 0),
        };
        var profile = new MontageProfile("montage", "Montage", "", MontageProfileSource.User, "device", "config", config,
            derived, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "fingerprint");
        return new LiveMontageSnapshot(profile, 2, 5120, ["F3-REF", "C3-REF"], "fingerprint");
    }
}
