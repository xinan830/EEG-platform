using System.Text.Json.Serialization;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

/// <summary>Drop zone inside the VS-style dock guide diamond or outer workspace edges.</summary>
public enum DockDropZone
{
    CenterTab,
    Left,
    Right,
    Top,
    Bottom,
    OuterLeft,
    OuterRight,
    OuterTop,
    OuterBottom,
}

/// <summary>Built-in workspace layout presets for quick switching.</summary>
public enum DockLayoutPreset
{
    AutoClassic,
    TopBottomWaveform,
    TabbedAnalysis,
}

/// <summary>Serializable tree node representing a docked workspace layout.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(DockPaneNode), "pane")]
[JsonDerivedType(typeof(DockTabGroupNode), "tabs")]
[JsonDerivedType(typeof(DockSplitNode), "split")]
public abstract class DockNode
{
    public abstract DockNode Clone();
}

public sealed class DockPaneNode : DockNode
{
    public DockPaneNode() { }
    public DockPaneNode(string paneId) => PaneId = paneId;

    public string PaneId { get; set; } = string.Empty;

    public override DockNode Clone() => new DockPaneNode(PaneId);
}

public sealed class DockTabGroupNode : DockNode
{
    public List<string> PaneIds { get; set; } = [];
    public string ActivePaneId { get; set; } = string.Empty;

    public override DockNode Clone() => new DockTabGroupNode
    {
        PaneIds = [.. PaneIds],
        ActivePaneId = ActivePaneId,
    };
}

public sealed class DockSplitNode : DockNode
{
    public Orientation Orientation { get; set; } = Orientation.Horizontal;
    public double FirstRatio { get; set; } = 0.5;
    public DockNode First { get; set; } = new DockPaneNode();
    public DockNode Second { get; set; } = new DockPaneNode();

    public override DockNode Clone() => new DockSplitNode
    {
        Orientation = Orientation,
        FirstRatio = FirstRatio,
        First = First.Clone(),
        Second = Second.Clone(),
    };
}

public sealed class DockLayoutSnapshot
{
    public int Version { get; set; } = 1;
    public bool IsCustomLayout { get; set; }
    public DockNode? Root { get; set; }
    public List<string> FloatingPaneIds { get; set; } = [];
}
