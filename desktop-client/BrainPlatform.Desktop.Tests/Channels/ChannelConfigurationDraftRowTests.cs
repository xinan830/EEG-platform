using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.ViewModels;

namespace BrainPlatform.Desktop.Tests.Channels;

public sealed class ChannelConfigurationDraftRowTests
{
    [Fact]
    public void ChangingLabelClearsLocationAndAdvancesRevisionEvenWhenLabelChangesBack()
    {
        var location = new ElectrodeLocationSnapshot(
            "Fp1", -0.029, 0.083, -0.006, "mri", "m", "mne.standard_1020", "1.12.1");
        var row = new ChannelConfigurationDraftRow(
            0, "EEG", "V", "µV", "Fp1", true, 0, true, location,
            new ElectrodeMapPosition(0.25, -0.4));

        row.ElectrodeLabel = "F3";
        var firstRevision = row.ElectrodeLabelRevision;
        Assert.Null(row.ManualMapPosition);
        row.ElectrodeLabel = "Fp1";

        Assert.Null(row.ElectrodeLocation);
        Assert.Null(row.ManualMapPosition);
        Assert.Equal("不可用 / 未解析", row.LocationText);
        Assert.True(row.ElectrodeLabelRevision > firstRevision);
    }
}
