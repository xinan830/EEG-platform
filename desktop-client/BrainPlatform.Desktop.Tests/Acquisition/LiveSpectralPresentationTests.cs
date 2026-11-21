using System.Text.Json;
using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;
using BrainPlatform.Desktop.Modules.Channels.Domain;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class LiveSpectralPresentationTests
{
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
            ], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var stream = new AcquisitionStreamMetadata("device", "device", 512,
            [new(0, 12, "F3", AcquisitionChannelKind.Reference, "V"),
             new(1, 14, "C3", AcquisitionChannelKind.Reference, "V"),
             new(2, 15, "F4", AcquisitionChannelKind.Bipolar, "V")],
            3, DateTimeOffset.UnixEpoch)
        {
            HardwareConfiguration = new Dictionary<string, string>
            {
                ["channel_configuration_snapshot_json"] = JsonSerializer.Serialize(profile),
            },
        };
        var config = LiveSpectralPresentation.MapConfig(stream, "psd.band-mean-density.alpha");
        var positions = config.GetProperty("positions");
        Assert.Equal(-0.3, positions.GetProperty("F3").GetProperty("x").GetDouble());
        Assert.False(positions.GetProperty("C3").GetProperty("enabled").GetBoolean());
        Assert.False(positions.TryGetProperty("F4", out _));
        Assert.Equal(new[] { "F3", "C3" }, config.GetProperty("spectrum").GetProperty("channels")
            .EnumerateArray().Select(channel => channel.GetString()));
    }

    private static LiveAnalysisUpdate Update(string algorithm, string payload) =>
        new(Guid.NewGuid(), algorithm, 1, "ready", 512, JsonSerializer.Deserialize<JsonElement>(payload), 13, 23);
}
