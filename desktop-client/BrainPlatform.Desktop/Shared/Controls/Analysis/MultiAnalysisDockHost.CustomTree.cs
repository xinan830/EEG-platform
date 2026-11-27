using System.Windows;
using System.Windows.Media;
using BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis;

public sealed partial class MultiAnalysisDockHost
{
    public void ApplyPreset(DockLayoutPreset preset)
    {
        if (preset == DockLayoutPreset.AutoClassic)
        {
            ResetToAutoLayout();
            return;
        }
        var analysisIds = Materialize(RightSlots).Concat(Materialize(BottomSlots)).Select(ResolvePaneId).ToList();
        customLayoutTree = DockTreeOperations.BuildPreset(preset, analysisIds);
        isCustomLayout = true;
        RenderActiveMode();
        SaveLayoutIfEnabled();
    }

    public void ResetToAutoLayout()
    {
        isCustomLayout = false;
        customLayoutTree = null;
        if (EnableLayoutPersistence) DockLayoutPersistence.Clear(PersistenceFilePath);
        RenderActiveMode();
    }

    internal void DockPaneAt(string paneId, string? targetPaneId, DockDropZone zone) =>
        HandleDropDock(paneId, targetPaneId, zone);

    private string PersistenceFilePath => DockLayoutPersistence.GetFilePath(LayoutPersistenceKey);

    private void RenderCustomTree()
    {
        var paneMap = BuildPaneChromeLookup();
        var available = paneMap.Keys.ToHashSet(StringComparer.Ordinal);
        // Project saved layout template onto currently open panes without discarding remembered slots when closed.
        var projected = DockTreeOperations.PruneToAvailablePanes(customLayoutTree, available);
        if (available.Contains(DockTreeOperations.WaveformPaneId) && !DockTreeOperations.ContainsPane(projected, DockTreeOperations.WaveformPaneId))
        {
            projected = DockTreeOperations.InsertPane(projected, DockTreeOperations.WaveformPaneId, null, DockDropZone.OuterLeft);
        }
        foreach (var id in available)
        {
            if (!DockTreeOperations.ContainsPane(projected, id))
            {
                projected = DockTreeOperations.InsertPane(projected, id, DockTreeOperations.WaveformPaneId, DockDropZone.Right);
            }
        }
        customLayoutTree ??= projected?.Clone();

        customSurface.Children.Clear();
        var rendered = treeRenderer.Render(
            projected,
            id => paneMap.TryGetValue(id, out var chrome) ? chrome : null,
            id => FindSlotByPaneId(id) is { } s && ReadBoolProperty(s, "IsFocused"),
            () => { customLayoutTree = projected?.Clone(); SaveLayoutIfEnabled(); },
            id => { if (FindSlotByPaneId(id) is { } s && FocusSlotCommand?.CanExecute(s) == true) FocusSlotCommand.Execute(s); },
            (id, title, _) => dragCoordinator.BeginDockedDrag(id, title));
        if (rendered is not null) customSurface.Children.Add(rendered);
    }

    private void EnsureCustomTreeInitialized()
    {
        if (isCustomLayout) return;
        var rightIds = Materialize(RightSlots).Select(ResolvePaneId).ToList();
        var bottomIds = Materialize(BottomSlots).Select(ResolvePaneId).ToList();
        var availableWidth = rightContainer.ActualWidth > 0 ? rightContainer.ActualWidth : workspace.ActualWidth * 0.5;
        customLayoutTree = DockTreeOperations.BuildFromAutoLayout(rightIds, bottomIds, availableWidth >= WideTwoColumnMinWidth);
        isCustomLayout = true;
    }

    private void HandleDropDock(string paneId, string? targetPaneId, DockDropZone zone)
    {
        EnsureCustomTreeInitialized();
        var floatingSlot = Materialize(FloatingSlots).FirstOrDefault(s => string.Equals(ResolvePaneId(s), paneId, StringComparison.Ordinal));
        customLayoutTree = DockTreeOperations.InsertPane(customLayoutTree, paneId, targetPaneId, zone);
        if (floatingSlot is not null)
        {
            if (DockFloatingSlotCommand?.CanExecute(floatingSlot) == true) DockFloatingSlotCommand.Execute(floatingSlot);
            else if (FloatingWindowClosedCommand?.CanExecute(floatingSlot) == true) FloatingWindowClosedCommand.Execute(floatingSlot);
        }
        RenderActiveMode();
        SaveLayoutIfEnabled();
    }

    private void HandleDropFloat(string paneId, Point cursorScreenPx)
    {
        if (FindSlotByPaneId(paneId) is not { } slot) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var dipPos = new Point(
            cursorScreenPx.X / (dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1),
            cursorScreenPx.Y / (dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1));
        floatingManager.SetNextWindowPosition(slot, dipPos);

        if (isCustomLayout) customLayoutTree = DockTreeOperations.RemovePane(customLayoutTree, paneId);
        if (FloatSlotCommand?.CanExecute(slot) == true) FloatSlotCommand.Execute(slot);
        SaveLayoutIfEnabled();
    }

    private void TryRestoreSavedLayout()
    {
        if (!EnableLayoutPersistence) return;
        var snapshot = DockLayoutPersistence.Load(PersistenceFilePath);
        if (snapshot is not { IsCustomLayout: true, Root: not null }) return;
        customLayoutTree = snapshot.Root;
        isCustomLayout = true;
        RenderActiveMode();
    }

    private void SaveLayoutIfEnabled()
    {
        if (!EnableLayoutPersistence) return;
        DockLayoutPersistence.Save(new DockLayoutSnapshot
        {
            IsCustomLayout = isCustomLayout,
            Root = customLayoutTree?.Clone(),
            FloatingPaneIds = Materialize(FloatingSlots).Select(ResolvePaneId).ToList(),
        }, PersistenceFilePath);
    }

    private Dictionary<string, DockCardChrome> BuildPaneChromeLookup()
    {
        var map = new Dictionary<string, DockCardChrome>(StringComparer.Ordinal);
        if (waveformChrome is not null) map[DockTreeOperations.WaveformPaneId] = waveformChrome;
        foreach (var (slot, chrome) in rightCardHosts.Concat(bottomCardHosts))
            map[ResolvePaneId(slot)] = chrome;
        return map;
    }

    private object? FindSlotByPaneId(string paneId) =>
        rightCardHosts.Keys.Concat(bottomCardHosts.Keys).Concat(Materialize(FloatingSlots))
            .FirstOrDefault(s => string.Equals(ResolvePaneId(s), paneId, StringComparison.Ordinal));

    private DockCardChrome CreatePaneChrome(string paneId, string title, FrameworkElement? directContent = null, object? slotData = null)
    {
        var chrome = new DockCardChrome(paneId, title, directContent, PanelTemplate, slotData);
        chrome.DragStarted += (id, label, _) =>
        {
            if (!isCustomLayout)
            {
                EnsureCustomTreeInitialized();
                RenderActiveMode();
                UpdateLayout();
            }
            dragCoordinator.BeginDockedDrag(id, label);
        };
        chrome.Activated += id =>
        {
            if (FindSlotByPaneId(id) is { } slot && FocusSlotCommand?.CanExecute(slot) == true) FocusSlotCommand.Execute(slot);
        };
        return chrome;
    }
}
