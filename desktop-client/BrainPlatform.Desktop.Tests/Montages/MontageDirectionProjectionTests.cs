using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Montages.ViewModels;
using BrainPlatform.Desktop.Modules.Montages.Views;

namespace BrainPlatform.Desktop.Tests.Montages;

public sealed class MontageDirectionProjectionTests
{
    [Fact]
    public void HardwareReferenceDrawsArrowFromPositiveElectrodeToRef()
    {
        var configuration = Configuration(
            Channel("F3", new ElectrodeMapPosition(-0.3, -0.2)));
        var row = Row("F3", MontageNegativeKind.OriginalHardwareReference);

        var arrow = MontageDirectionProjection.Create(configuration, [row], []).Single();

        Assert.Equal(("F3", "REF"), (arrow.FromLabel, arrow.ToLabel));
        Assert.Equal(-0.3, arrow.FromX);
        Assert.Equal(ElectrodeMapDefaults.Reference.X, arrow.ToX);
    }

    [Fact]
    public void ChannelAndAverageReferencesPointTowardEachNegativeElectrode()
    {
        var configuration = Configuration(
            Channel("F3", new ElectrodeMapPosition(-0.3, -0.2)),
            Channel("C3", new ElectrodeMapPosition(-0.4, 0)),
            Channel("C4", new ElectrodeMapPosition(0.4, 0)));
        var channelRow = Row("F3", MontageNegativeKind.Channel, "C3");
        var averageRow = Row("F3", MontageNegativeKind.Mean);

        var arrows = MontageDirectionProjection.Create(configuration, [channelRow, averageRow], ["C3", "C4"]);

        Assert.Equal(new[] { "C3", "C3", "C4" }, arrows.Select(arrow => arrow.ToLabel));
        Assert.All(arrows, arrow => Assert.Equal("F3", arrow.FromLabel));
    }

    [Fact]
    public void MapPointsIncludeMappedElectrodesEvenWhenNotSelectedForDisplayOrUsedByMontageRows()
    {
        var hiddenElectrode = new ChannelConfigurationEntry(
            19, AcquisitionChannelKind.Reference, "O1", false, 19,
            ManualMapPosition: new ElectrodeMapPosition(-0.28, 0.85));
        var unreferencedElectrode = Channel("O2", new ElectrodeMapPosition(0.28, 0.85));
        var configuration = Configuration(Channel("F3", new ElectrodeMapPosition(-0.3, -0.2)), hiddenElectrode, unreferencedElectrode);

        var points = MontageDirectionProjection.CreatePoints(configuration);

        Assert.Contains(points, point => point.Label == "O1");
        Assert.Contains(points, point => point.Label == "O2");
        Assert.DoesNotContain(MontageDirectionProjection.Create(configuration, [Row("F3", MontageNegativeKind.OriginalHardwareReference)], []),
            arrow => arrow.FromLabel is "O1" or "O2" || arrow.ToLabel is "O1" or "O2");
    }

    [Fact]
    public void StandardO1AndO2UseMneLayoutCoordinatesInTheScalpMap()
    {
        var configuration = Configuration(
            StandardChannel("O1", -0.0294134, -0.1124490, 0.008839, -0.20944567100473832, 0.5344564790544133),
            StandardChannel("O2", 0.0298426, -0.1121560, 0.0088, 0.18317502629299898, 0.5325828772907352));

        var points = MontageDirectionProjection.CreatePoints(configuration);

        Assert.Contains(points, point => point.Label == "O1" && point.X == -0.20944567100473832 && point.Y == 0.5344564790544133);
        Assert.Contains(points, point => point.Label == "O2" && point.X == 0.18317502629299898 && point.Y == 0.5325828772907352);
    }

    private static ChannelConfigurationProfile Configuration(params ChannelConfigurationEntry[] channels) => new(
        "test", "test", "", ChannelConfigurationSource.User, "device", channels,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static ChannelConfigurationEntry Channel(string label, ElectrodeMapPosition position) => new(
        0, AcquisitionChannelKind.Reference, label, true, 0, ManualMapPosition: position);

    private static ChannelConfigurationEntry StandardChannel(string label, double x, double y, double z, double mapX, double mapY) => new(
        0, AcquisitionChannelKind.Reference, label, true, 0,
        new ElectrodeLocationSnapshot(label, x, y, z, "mri", "m", "mne.colin27_1020", "1.12.1", mapX, mapY));

    private static MontageDraftRow Row(string positive, MontageNegativeKind kind, string? negative = null) => new(
        positive, 0, ["F3", "C3", "C4"], kind, negative, null, null, "AVG", "PAIR");
}
