using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Acquisition.ViewModels;
using BrainPlatform.Desktop.Modules.Events.Views;
using BrainPlatform.Desktop.Modules.Montages.Views;
using BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

namespace BrainPlatform.Desktop.Modules.Acquisition.Views;

public partial class AcquisitionWorkspaceView : UserControl
{
    private LiveAnalysisWorkspaceViewModel? subscribedLiveAnalysis;

    public AcquisitionWorkspaceView()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachLiveAnalysisEvents();
        Unloaded += (_, _) => DetachLiveAnalysisEvents();
        DataContextChanged += (_, _) => { if (IsLoaded) AttachLiveAnalysisEvents(); };
    }

    private void OnBackClick(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as EegSessionWindow)?.Close();

    private async void OnImpedanceClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace) return;
        try { await workspace.Acquisition.ShowImpedanceAsync(Window.GetWindow(this)); }
        catch (Exception exception) { workspace.Notifications.PublishError(exception.Message); }
    }

    private async void OnFinishRecordingClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace)
        {
            return;
        }

        var sessionWindow = Window.GetWindow(this) as EegSessionWindow;
        try
        {
            await workspace.Acquisition.FinishAcquisitionAsync();
            workspace.Projects.RefreshRecordings();
            sessionWindow?.CloseCompletedSession();
        }
        catch (Exception exception)
        {
            workspace.Notifications.PublishError(exception.Message);
        }
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace || !workspace.Acquisition.LiveMonitor.IsRecording)
        {
            return;
        }

        var definition = EventShortcutInput.Match(
            workspace.Acquisition.EnabledEventDefinitions, ShortcutScope.Acquisition, e, out var shortcut);
        if (definition is null) return;
        e.Handled = true;
        try
        {
            await workspace.Acquisition.TriggerEventFromGridAsync(definition, null, EventSource.KeyboardShortcut, shortcut);
        }
        catch (Exception exception)
        {
            workspace.Notifications.PublishError(exception.Message);
        }
    }

    private void OnOnlineMontageClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace) return;
        if (workspace.Acquisition.OnlineMontageSource is not { } source) return;
        var dialog = new TemporaryMontageEditorWindow(source, workspace.Acquisition.TemporaryWaveformMontage, Window.GetWindow(this));
        if (dialog.ShowDialog() != true || dialog.SavedProfile is not { } profile) return;
        try { workspace.Acquisition.SetTemporaryWaveformMontage(profile); }
        catch (Exception exception) { workspace.Notifications.PublishError(exception.Message); }
    }

    private void OnLayoutPresetClick(object sender, RoutedEventArgs e) => DockLayoutPresetMenu.Open(sender);

    private void OnSelectLayoutPresetClick(object sender, RoutedEventArgs e) => DockLayoutPresetMenu.Apply(AcquisitionAnalysisHost, sender);

    private void AttachLiveAnalysisEvents()
    {
        var liveAnalysis = (DataContext as DesktopWorkspaceViewModel)?.Acquisition.LiveAnalysis;
        if (ReferenceEquals(subscribedLiveAnalysis, liveAnalysis)) return;
        DetachLiveAnalysisEvents();
        subscribedLiveAnalysis = liveAnalysis;
        if (subscribedLiveAnalysis is not null)
            subscribedLiveAnalysis.ActivateFloatingSlotRequested += OnActivateFloatingSlotRequested;
    }

    private void DetachLiveAnalysisEvents()
    {
        if (subscribedLiveAnalysis is null) return;
        subscribedLiveAnalysis.ActivateFloatingSlotRequested -= OnActivateFloatingSlotRequested;
        subscribedLiveAnalysis = null;
    }

    private void OnActivateFloatingSlotRequested(object? sender, LiveAnalysisCardViewModel slot) =>
        AcquisitionAnalysisHost.ActivateFloatingPanel(slot);
}
