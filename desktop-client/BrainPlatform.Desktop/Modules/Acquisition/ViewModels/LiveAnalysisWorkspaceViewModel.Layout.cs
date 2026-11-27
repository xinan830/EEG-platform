using System.Collections.ObjectModel;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

/// <summary>Dock collections, active focus, maximize, and floating-window coordination for live cards.</summary>
public sealed partial class LiveAnalysisWorkspaceViewModel
{
    public event EventHandler<LiveAnalysisCardViewModel>? ActivateFloatingSlotRequested;

    public ObservableCollection<LiveAnalysisCardViewModel> OpenSlots { get; } = [];
    public ObservableCollection<LiveAnalysisCardViewModel> RightDockedSlots { get; } = [];
    public ObservableCollection<LiveAnalysisCardViewModel> BottomDockedSlots { get; } = [];
    public ObservableCollection<LiveAnalysisCardViewModel> FloatingSlots { get; } = [];

    public LiveAnalysisCardViewModel? ActiveSlot => activeSlot;
    public ICommand OpenRbpCommand { get; }
    public ICommand OpenPsdCommand { get; }
    public ICommand OpenStftCommand { get; }
    public ICommand FocusSlotCommand { get; }
    public ICommand FloatingWindowClosedCommand { get; }
    public ICommand FloatSlotCommand { get; }
    public ICommand DockFloatingSlotCommand { get; }

    public bool IsRbpOpen => GetOpenSlot("rbp") is not null;
    public bool IsPsdOpen => GetOpenSlot("psd") is not null;
    public bool IsStftOpen => GetOpenSlot("stft") is not null;
    public bool IsOpen => OpenSlots.Count > 0;

    internal LiveAnalysisCardViewModel? GetOpenSlot(string algorithmId) =>
        OpenSlots.FirstOrDefault(slot => string.Equals(slot.AlgorithmId, algorithmId, StringComparison.Ordinal));

    public void FocusSlot(string algorithmId)
    {
        if (GetOpenSlot(algorithmId) is not { } slot) return;
        if (!slot.IsFloating && RightDockedSlots.Any(right => right.IsMaximized) && !slot.IsMaximized)
        {
            foreach (var right in RightDockedSlots) right.IsMaximized = false;
        }
        SetActiveSlot(slot);
        if (slot.IsFloating) ActivateFloatingSlotRequested?.Invoke(this, slot);
    }

    public void ToggleMaximizeSlot(string algorithmId)
    {
        if (GetOpenSlot(algorithmId) is not { } slot || !slot.CanMaximize) return;
        var next = !slot.IsMaximized;
        foreach (var right in RightDockedSlots) right.IsMaximized = ReferenceEquals(right, slot) && next;
        SetActiveSlot(slot);
    }

    public void ToggleFloatingSlot(string algorithmId)
    {
        if (GetOpenSlot(algorithmId) is not { } slot) return;
        if (!slot.IsFloating)
        {
            slot.IsFloating = true;
            RebuildDockCollections();
            SetActiveSlot(slot);
            return;
        }
        DockFloatingSlotDirect(slot);
    }

    internal void DockFloatingSlotDirect(LiveAnalysisCardViewModel slot)
    {
        if (disposed || slot.IsClosed || !slot.IsFloating) return;
        slot.IsFloating = false;
        foreach (var right in OpenSlots) right.IsMaximized = false;
        RebuildDockCollections();
        SetActiveSlot(slot);
    }

    internal void HandleFloatingWindowClosedByUser(LiveAnalysisCardViewModel slot)
    {
        if (disposed || slot.IsClosed || !slot.IsFloating) return;
        DockFloatingSlotDirect(slot);
    }

    private void CloseAllSlotsLocally()
    {
        if (OpenSlots.Count == 0) return;
        foreach (var slot in OpenSlots) slot.IsClosed = true;
        OpenSlots.Clear();
        RebuildDockCollections();
        SetActiveSlot(null);
    }

    private void SetActiveSlot(LiveAnalysisCardViewModel? slot)
    {
        if (ReferenceEquals(activeSlot, slot)) return;
        if (activeSlot is not null) activeSlot.IsFocused = false;
        activeSlot = slot;
        if (activeSlot is not null) activeSlot.IsFocused = true;
        RaisePropertyChanged(nameof(ActiveSlot));
    }

    private void RebuildDockCollections()
    {
        SyncCollection(RightDockedSlots, OpenSlots.Where(s => !s.IsFloating));
        BottomDockedSlots.Clear();
        SyncCollection(FloatingSlots, OpenSlots.Where(s => s.IsFloating));
        RaisePropertyChanged(nameof(IsRbpOpen));
        RaisePropertyChanged(nameof(IsPsdOpen));
        RaisePropertyChanged(nameof(IsStftOpen));
        RaisePropertyChanged(nameof(IsOpen));
    }

    private static void SyncCollection(ObservableCollection<LiveAnalysisCardViewModel> target, IEnumerable<LiveAnalysisCardViewModel> desired)
    {
        var list = desired.ToList();
        for (var i = target.Count - 1; i >= 0; i--)
            if (!list.Contains(target[i])) target.RemoveAt(i);
        foreach (var item in list)
            if (!target.Contains(item)) target.Add(item);
    }
}
