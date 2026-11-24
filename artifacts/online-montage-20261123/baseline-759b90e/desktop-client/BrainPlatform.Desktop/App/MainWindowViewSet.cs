using BrainPlatform.Desktop.Modules.Acquisition.Views;
using BrainPlatform.Desktop.Modules.Algorithms.Views;
using BrainPlatform.Desktop.Modules.Channels.Views;
using BrainPlatform.Desktop.Modules.Events.Views;
using BrainPlatform.Desktop.Modules.Montages.Views;
using BrainPlatform.Desktop.Modules.Projects.Views;
using BrainPlatform.Desktop.Modules.Settings.Views;

namespace BrainPlatform.Desktop.Application;

/// <summary>Application-owned construction of the shell's persistent feature views.</summary>
public sealed class MainWindowViewSet
{
    public AcquisitionPreparationView AcquisitionPreparation { get; } = new();
    public DeviceOverviewView Overview { get; } = new();
    public ProjectListView ProjectList { get; } = new();
    public ProjectDetailView ProjectDetail { get; } = new();
    public SettingsView Settings { get; } = new();
    public ScreenCalibrationView ScreenCalibration { get; } = new();
    public ChannelListView ChannelList { get; } = new();
    public ChannelDetailView ChannelDetail { get; } = new();
    public MontageListView MontageList { get; } = new();
    public MontageDetailView MontageDetail { get; } = new();
    public EventListView EventList { get; } = new();
    public EventDefinitionEditorView EventDefinitionEditor { get; } = new();
    public AlgorithmWorkspaceView AlgorithmWorkspace { get; } = new();
}
