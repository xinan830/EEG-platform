using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

/// <summary>Shared layout preset ContextMenu helpers for multi-panel dock hosts.</summary>
public static class DockLayoutPresetMenu
{
    public static void Open(object sender)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 0;
            menu.IsOpen = true;
        }
    }

    public static void Apply(MultiAnalysisDockHost host, object sender)
    {
        if (sender is not MenuItem { Tag: string tag }) return;
        switch (tag)
        {
            case "TopBottomWaveform":
                host.ApplyPreset(DockLayoutPreset.TopBottomWaveform);
                break;
            case "TabbedAnalysis":
                host.ApplyPreset(DockLayoutPreset.TabbedAnalysis);
                break;
            case "AutoClassic":
            case "Reset":
                host.ResetToAutoLayout();
                break;
        }
    }
}
