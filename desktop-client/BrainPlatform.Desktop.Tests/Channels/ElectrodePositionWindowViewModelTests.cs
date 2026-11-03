using BrainPlatform.Desktop.Modules.Channels.ViewModels;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Tests.Channels;

public sealed class ElectrodePositionWindowViewModelTests
{
    [Fact]
    public void Three_dimensional_preview_tracks_only_enabled_eeg_draft_labels()
    {
        var configuration = new ChannelConfigurationWorkspaceViewModel(new ChannelMappingViewModel());
        using var viewModel = new ElectrodePositionWindowViewModel(configuration, null);
        var enabled = new ChannelConfigurationDraftRow(0, "EEG", "V", "µV", "Fp1", true, 0, true);
        var disabled = new ChannelConfigurationDraftRow(1, "EEG", "V", "µV", "F3", false, 1, true);
        var reference = new ChannelConfigurationDraftRow(2, "REF", "V", "µV", "A1", true, 2, false);

        Assert.False(viewModel.HasPositionRows);
        Assert.True(viewModel.IsTwoDimensional);
        configuration.DraftRows.Add(enabled);
        configuration.DraftRows.Add(disabled);
        configuration.DraftRows.Add(reference);

        Assert.True(viewModel.HasPositionRows);
        Assert.Equal(["Fp1"], viewModel.EnabledElectrodeLabels);
        viewModel.IsThreeDimensional = true;
        Assert.False(viewModel.IsTwoDimensional);

        enabled.IsEnabled = false;

        Assert.Empty(viewModel.EnabledElectrodeLabels);
        Assert.Contains("切换到 3D", viewModel.PositionViewHint);
    }

    [Fact]
    public void Saved_profile_positions_remain_visible_without_a_loaded_device_draft()
    {
        var configuration = new ChannelConfigurationWorkspaceViewModel(new ChannelMappingViewModel());
        using var viewModel = new ElectrodePositionWindowViewModel(configuration, null);
        configuration.SelectedProfile = new ChannelConfigurationProfile(
            "profile-1", "保存配置", string.Empty, ChannelConfigurationSource.User, "device-1",
            [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Unknown, "Fp1", true, 0,
                ManualMapPosition: new ElectrodeMapPosition(-0.1, 0.2))],
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.Empty(configuration.DraftRows);
        Assert.True(viewModel.HasPositionRows);
        Assert.False(viewModel.HasEditableDraftRows);
        Assert.Equal(["Fp1"], viewModel.EnabledElectrodeLabels);
        Assert.Equal(new ElectrodeMapPosition(-0.1, 0.2), viewModel.PositionRows[0].ManualMapPosition);
    }
}
