using System.Windows;
using BrainPlatform.Desktop.Modules.Channels.ViewModels;

namespace BrainPlatform.Desktop.Modules.Channels.Views;

public partial class ElectrodePositionWindow : Window
{
    public ElectrodePositionWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    private async void OnThreeDimensionalChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is ElectrodePositionWindowViewModel viewModel)
            await viewModel.LoadModelAsync();
    }

    private void ResetAllPositions_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in ElectrodeRows.Items.OfType<ChannelConfigurationDraftRow>())
        {
            row.ResetToTemplatePosition();
        }
        if (DataContext is ElectrodePositionWindowViewModel viewModel)
        {
            viewModel.ChannelConfigurations.ResetDraftMapPositionsToDefaults();
        }
    }
}
