using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

/// <summary>Pure structural operations and JSON serialization on <see cref="DockNode"/> trees.</summary>
public static class DockTreeOperations
{
    public const string WaveformPaneId = "__waveform__";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string SerializeSnapshot(DockLayoutSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);

    public static DockLayoutSnapshot? DeserializeSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<DockLayoutSnapshot>(json, JsonOptions); }
        catch { return null; }
    }

    public static bool ContainsPane(DockNode? node, string paneId) => node switch
    {
        DockPaneNode pane => string.Equals(pane.PaneId, paneId, StringComparison.Ordinal),
        DockTabGroupNode tabs => tabs.PaneIds.Any(id => string.Equals(id, paneId, StringComparison.Ordinal)),
        DockSplitNode split => ContainsPane(split.First, paneId) || ContainsPane(split.Second, paneId),
        _ => false,
    };

    public static IEnumerable<string> EnumeratePaneIds(DockNode? node)
    {
        switch (node)
        {
            case DockPaneNode pane when !string.IsNullOrWhiteSpace(pane.PaneId):
                yield return pane.PaneId;
                break;
            case DockTabGroupNode tabs:
                foreach (var id in tabs.PaneIds)
                {
                    if (!string.IsNullOrWhiteSpace(id)) yield return id;
                }
                break;
            case DockSplitNode split:
                foreach (var id in EnumeratePaneIds(split.First)) yield return id;
                foreach (var id in EnumeratePaneIds(split.Second)) yield return id;
                break;
        }
    }

    public static DockNode? RemovePane(DockNode? node, string paneId)
    {
        switch (node)
        {
            case null:
                return null;
            case DockPaneNode pane:
                return string.Equals(pane.PaneId, paneId, StringComparison.Ordinal) ? null : pane;
            case DockTabGroupNode tabs:
            {
                var remaining = tabs.PaneIds.Where(id => !string.Equals(id, paneId, StringComparison.Ordinal)).ToList();
                if (remaining.Count == 0) return null;
                if (remaining.Count == 1) return new DockPaneNode(remaining[0]);
                return new DockTabGroupNode
                {
                    PaneIds = remaining,
                    ActivePaneId = remaining.Contains(tabs.ActivePaneId, StringComparer.Ordinal) ? tabs.ActivePaneId : remaining[0],
                };
            }
            case DockSplitNode split:
            {
                var first = RemovePane(split.First, paneId);
                var second = RemovePane(split.Second, paneId);
                if (first is null) return second;
                if (second is null) return first;
                return new DockSplitNode
                {
                    Orientation = split.Orientation,
                    FirstRatio = ClampRatio(split.FirstRatio),
                    First = first,
                    Second = second,
                };
            }
            default:
                return node;
        }
    }

    public static DockNode InsertPane(
        DockNode? root,
        string paneId,
        string? targetPaneId,
        DockDropZone zone,
        double ratio = 0.5)
    {
        var cleaned = RemovePane(root, paneId);
        var incoming = new DockPaneNode(paneId);
        if (cleaned is null) return incoming;

        if (zone is DockDropZone.OuterLeft or DockDropZone.OuterRight or DockDropZone.OuterTop or DockDropZone.OuterBottom)
        {
            return WrapWithSplit(cleaned, incoming, zone, ClampRatio(ratio));
        }

        if (string.IsNullOrWhiteSpace(targetPaneId) ||
            string.Equals(targetPaneId, paneId, StringComparison.Ordinal) ||
            !ContainsPane(cleaned, targetPaneId))
        {
            return WrapWithSplit(cleaned, incoming, DockDropZone.Right, ClampRatio(ratio));
        }

        return InsertIntoTarget(cleaned, incoming, paneId, targetPaneId, zone, ClampRatio(ratio));
    }

    public static DockNode? PruneToAvailablePanes(DockNode? node, IReadOnlySet<string> availablePaneIds)
    {
        switch (node)
        {
            case null:
                return null;
            case DockPaneNode pane:
                return availablePaneIds.Contains(pane.PaneId) ? pane.Clone() : null;
            case DockTabGroupNode tabs:
            {
                var valid = tabs.PaneIds.Where(availablePaneIds.Contains).Distinct(StringComparer.Ordinal).ToList();
                if (valid.Count == 0) return null;
                if (valid.Count == 1) return new DockPaneNode(valid[0]);
                return new DockTabGroupNode
                {
                    PaneIds = valid,
                    ActivePaneId = valid.Contains(tabs.ActivePaneId, StringComparer.Ordinal) ? tabs.ActivePaneId : valid[0],
                };
            }
            case DockSplitNode split:
            {
                var first = PruneToAvailablePanes(split.First, availablePaneIds);
                var second = PruneToAvailablePanes(split.Second, availablePaneIds);
                if (first is null) return second;
                if (second is null) return first;
                return new DockSplitNode
                {
                    Orientation = split.Orientation,
                    FirstRatio = ClampRatio(split.FirstRatio),
                    First = first,
                    Second = second,
                };
            }
            default:
                return null;
        }
    }

    public static DockNode BuildFromAutoLayout(
        IReadOnlyList<string> rightPaneIds,
        IReadOnlyList<string> bottomPaneIds,
        bool wideRight)
    {
        DockNode topNode = new DockPaneNode(WaveformPaneId);
        if (rightPaneIds.Count > 0)
        {
            topNode = new DockSplitNode
            {
                Orientation = Orientation.Horizontal,
                FirstRatio = 0.5,
                First = topNode,
                Second = BuildRightSubtree(rightPaneIds, wideRight),
            };
        }

        if (bottomPaneIds.Count > 0)
        {
            return new DockSplitNode
            {
                Orientation = Orientation.Vertical,
                FirstRatio = 0.68,
                First = topNode,
                Second = BuildTabOrSingle(bottomPaneIds),
            };
        }

        return topNode;
    }

    public static DockNode BuildPreset(DockLayoutPreset preset, IReadOnlyList<string> analysisPaneIds)
    {
        if (analysisPaneIds.Count == 0) return new DockPaneNode(WaveformPaneId);

        return preset switch
        {
            DockLayoutPreset.TopBottomWaveform => new DockSplitNode
            {
                Orientation = Orientation.Vertical,
                FirstRatio = 0.55,
                First = new DockPaneNode(WaveformPaneId),
                Second = BuildHorizontalRow(analysisPaneIds),
            },
            DockLayoutPreset.TabbedAnalysis => new DockSplitNode
            {
                Orientation = Orientation.Horizontal,
                FirstRatio = 0.52,
                First = new DockPaneNode(WaveformPaneId),
                Second = BuildTabOrSingle(analysisPaneIds),
            },
            _ => BuildFromAutoLayout(analysisPaneIds, [], wideRight: true),
        };
    }

    public static double ClampRatio(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.12, 0.88) : 0.5;

    private static DockNode InsertIntoTarget(
        DockNode node,
        DockPaneNode incoming,
        string paneId,
        string targetPaneId,
        DockDropZone zone,
        double ratio)
    {
        switch (node)
        {
            case DockPaneNode pane when string.Equals(pane.PaneId, targetPaneId, StringComparison.Ordinal):
                if (zone == DockDropZone.CenterTab)
                    return new DockTabGroupNode { PaneIds = [pane.PaneId, paneId], ActivePaneId = paneId };
                return WrapWithSplit(pane, incoming, zone, ratio);

            case DockTabGroupNode tabs when tabs.PaneIds.Any(id => string.Equals(id, targetPaneId, StringComparison.Ordinal)):
                if (zone == DockDropZone.CenterTab)
                {
                    var next = tabs.PaneIds.Where(id => !string.Equals(id, paneId, StringComparison.Ordinal)).ToList();
                    next.Add(paneId);
                    return new DockTabGroupNode { PaneIds = next, ActivePaneId = paneId };
                }
                return WrapWithSplit(tabs, incoming, zone, ratio);

            case DockSplitNode split:
                return new DockSplitNode
                {
                    Orientation = split.Orientation,
                    FirstRatio = ClampRatio(split.FirstRatio),
                    First = InsertIntoTarget(split.First, incoming, paneId, targetPaneId, zone, ratio),
                    Second = InsertIntoTarget(split.Second, incoming, paneId, targetPaneId, zone, ratio),
                };

            default:
                return node;
        }
    }

    private static DockSplitNode WrapWithSplit(DockNode existing, DockNode incoming, DockDropZone zone, double ratio)
    {
        var orientation = zone is DockDropZone.Left or DockDropZone.Right or DockDropZone.OuterLeft or DockDropZone.OuterRight
            ? Orientation.Horizontal
            : Orientation.Vertical;
        var incomingFirst = zone is DockDropZone.Left or DockDropZone.Top or DockDropZone.OuterLeft or DockDropZone.OuterTop;
        return new DockSplitNode
        {
            Orientation = orientation,
            FirstRatio = incomingFirst ? ratio : 1.0 - ratio,
            First = incomingFirst ? incoming : existing,
            Second = incomingFirst ? existing : incoming,
        };
    }

    private static DockNode BuildRightSubtree(IReadOnlyList<string> ids, bool wideRight)
    {
        if (ids.Count == 1) return new DockPaneNode(ids[0]);
        if (ids.Count == 2)
        {
            return new DockSplitNode
            {
                Orientation = Orientation.Vertical, FirstRatio = 0.5,
                First = new DockPaneNode(ids[0]), Second = new DockPaneNode(ids[1]),
            };
        }
        if (!wideRight) return BuildTabOrSingle(ids);

        var topRow = new DockSplitNode
        {
            Orientation = Orientation.Horizontal, FirstRatio = 0.5,
            First = new DockPaneNode(ids[0]), Second = new DockPaneNode(ids[1]),
        };
        DockNode bottomRow = ids.Count == 3
            ? new DockPaneNode(ids[2])
            : new DockSplitNode
            {
                Orientation = Orientation.Horizontal, FirstRatio = 0.5,
                First = new DockPaneNode(ids[2]), Second = new DockPaneNode(ids[3]),
            };
        return new DockSplitNode { Orientation = Orientation.Vertical, FirstRatio = 0.5, First = topRow, Second = bottomRow };
    }

    private static DockNode BuildHorizontalRow(IReadOnlyList<string> ids)
    {
        if (ids.Count == 1) return new DockPaneNode(ids[0]);
        if (ids.Count <= 3)
        {
            DockNode acc = new DockPaneNode(ids[^1]);
            for (var i = ids.Count - 2; i >= 0; i--)
            {
                acc = new DockSplitNode
                {
                    Orientation = Orientation.Horizontal,
                    FirstRatio = 1.0 / (ids.Count - i),
                    First = new DockPaneNode(ids[i]),
                    Second = acc,
                };
            }
            return acc;
        }
        return BuildTabOrSingle(ids);
    }

    private static DockNode BuildTabOrSingle(IReadOnlyList<string> ids) =>
        ids.Count == 1 ? new DockPaneNode(ids[0]) : new DockTabGroupNode { PaneIds = [.. ids], ActivePaneId = ids[0] };
}
