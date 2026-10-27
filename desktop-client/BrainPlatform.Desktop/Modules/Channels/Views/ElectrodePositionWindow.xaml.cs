using System.Windows;
using BrainPlatform.Desktop.Modules.Channels.ViewModels;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Modules.Channels.Views;

public partial class ElectrodePositionWindow : Window
{
    public ElectrodePositionWindow()
    {
        InitializeComponent();
    }

    private void ResetAllPositions_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in ElectrodeRows.Items.OfType<ChannelConfigurationDraftRow>())
        {
            row.ResetToTemplatePosition();
        }
        if (DataContext is DesktopWorkspaceViewModel workspace)
        {
            workspace.ChannelConfigurations.ResetDraftMapPositionsToDefaults();
        }
    }
}
