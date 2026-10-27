using System.Windows;
using BrainPlatform.Desktop.Modules.Montages.ViewModels;

namespace BrainPlatform.Desktop.Modules.Montages.Views;

public partial class MontageDirectionWindow : Window
{
    public MontageDirectionWindow(MontageConfigurationWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        InitializeComponent();
        var configuration = workspace.DraftChannelConfiguration
            ?? throw new InvalidOperationException("请先选择导联适用的通道配置。");
        var averageLabels = workspace.AverageReferenceChannels
            .Where(option => option.IsIncluded)
            .Select(option => option.Label)
            .ToArray();
        DirectionMap.Arrows = MontageDirectionProjection.Create(configuration, workspace.DraftRows, averageLabels);
        DirectionMap.Points = MontageDirectionProjection.CreatePoints(configuration);
    }
}
