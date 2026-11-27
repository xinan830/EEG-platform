using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Slot layout collections, focus routing, maximize, and floating-window coordination.</summary>
public sealed partial class RecordingReviewViewModel
{
    public const int MaxRightDockedPanels = 4;
    private ReviewAnalysisCardViewModel? activeAnalysisSlot;
    private string fallbackAnalysisPreparationText = "算法未准备。";
    private double fallbackAnalysisPanelStartSeconds;

    public event EventHandler<ReviewAnalysisCardViewModel>? ActivateFloatingSlotRequested;

    public ObservableCollection<ReviewAnalysisCardViewModel> OpenAnalysisSlots { get; } = [];
    public ObservableCollection<ReviewAnalysisCardViewModel> RightDockedAnalysisSlots { get; } = [];
    public ObservableCollection<ReviewAnalysisCardViewModel> BottomDockedAnalysisSlots { get; } = [];
    public ObservableCollection<ReviewAnalysisCardViewModel> FloatingAnalysisSlots { get; } = [];

    public ReviewAnalysisCardViewModel? ActiveAnalysisSlot => activeAnalysisSlot;
    public System.Windows.Input.ICommand FocusAnalysisSlotCommand =>
        field ??= new RelayCommand<ReviewAnalysisCardViewModel>(slot => { if (slot is not null) FocusAnalysisSlot(slot.AlgorithmId); });
    public System.Windows.Input.ICommand FloatingWindowClosedCommand =>
        field ??= new RelayCommand<ReviewAnalysisCardViewModel>(slot => { if (slot is not null) HandleFloatingWindowClosedByUser(slot); });
    public System.Windows.Input.ICommand FloatAnalysisSlotCommand =>
        field ??= new RelayCommand<ReviewAnalysisCardViewModel>(slot => { if (slot is not null && !slot.IsFloating) ToggleFloatingSlot(slot.AlgorithmId); });
    public System.Windows.Input.ICommand DockFloatingAnalysisSlotCommand =>
        field ??= new RelayCommand<ReviewAnalysisCardViewModel>(slot => { if (slot is not null) DockFloatingSlotDirect(slot); });
    public bool IsRightAnalysisDockOpen => RightDockedAnalysisSlots.Count > 0;
    public bool IsBottomAnalysisDockOpen => BottomDockedAnalysisSlots.Count > 0;
    public int RightDockedPanelCount => RightDockedAnalysisSlots.Count;
    public ReviewAnalysisCardViewModel? MaximizedRightAnalysisSlot =>
        RightDockedAnalysisSlots.FirstOrDefault(slot => slot.IsMaximized);
    public bool IsAnyRightPanelMaximized => MaximizedRightAnalysisSlot is not null;

    // Ribbon highlight flags (reflect whether each algorithm is open anywhere)
    public bool IsRbpSlotOpen => GetOpenSlot("rbp") is not null;
    public bool IsPsdSlotOpen => GetOpenSlot("psd") is not null;
    public bool IsSpectralValuesSlotOpen => GetOpenSlot(SpectralValuesPanelId) is not null;
    public bool IsCustomFrequencySlotOpen => GetOpenSlot(CustomFrequencyInspectionPanelId) is not null;
    public bool IsStftSlotOpen => GetOpenSlot("stft") is not null;
    public bool IsFaaSlotOpen => GetOpenSlot("faa") is not null;
    public bool IsIapfSlotOpen => GetOpenSlot("iapf") is not null;
    public bool IsThetaBetaSlotOpen => GetOpenSlot("theta_beta") is not null;
    public bool IsBrainbeatSlotOpen => GetOpenSlot("brainbeat") is not null;
    public bool IsIndividualizedBrainbeatSlotOpen => GetOpenSlot("brainbeat_individualized") is not null;
    public bool IsTnkSlotOpen => GetOpenSlot("tnk_biofeedback") is not null;

    internal ReviewAnalysisCardViewModel? GetOpenSlot(string algorithmId) =>
        OpenAnalysisSlots.FirstOrDefault(slot => string.Equals(slot.AlgorithmId, algorithmId, StringComparison.Ordinal));

    public void FocusAnalysisSlot(string algorithmId)
    {
        if (GetOpenSlot(algorithmId) is not { } slot) return;
        if (!slot.IsFloating && !slot.IsBottomPreferred && IsAnyRightPanelMaximized && !slot.IsMaximized)
        {
            foreach (var right in RightDockedAnalysisSlots) right.IsMaximized = false;
        }
        SetActiveSlot(slot);
        if (slot.IsFloating) ActivateFloatingSlotRequested?.Invoke(this, slot);
        RaiseLayoutPropertiesChanged();
    }

    public void ToggleMaximizeSlot(string algorithmId)
    {
        if (GetOpenSlot(algorithmId) is not { } slot || !slot.CanMaximize) return;
        var next = !slot.IsMaximized;
        foreach (var right in RightDockedAnalysisSlots)
            right.IsMaximized = ReferenceEquals(right, slot) && next;
        SetActiveSlot(slot);
        RaiseLayoutPropertiesChanged();
    }

    public void ToggleFloatingSlot(string algorithmId)
    {
        if (GetOpenSlot(algorithmId) is not { } slot) return;
        if (!slot.IsFloating)
        {
            slot.IsFloating = true;
            RebuildDockCollections();
            SetActiveSlot(slot);
            RaiseLayoutPropertiesChanged();
            return;
        }

        if (!slot.IsBottomPreferred && RightDockedAnalysisSlots.Count >= MaxRightDockedPanels)
        {
            notifications?.PublishInformation($"右侧停靠区最多同屏展示 {MaxRightDockedPanels} 个算法，请先关闭或弹出其他面板。");
            ActivateFloatingSlotRequested?.Invoke(this, slot);
            return;
        }

        slot.IsFloating = false;
        if (!slot.IsBottomPreferred)
        {
            foreach (var right in OpenAnalysisSlots.Where(item => !item.IsBottomPreferred))
                right.IsMaximized = false;
        }
        RebuildDockCollections();
        SetActiveSlot(slot);
        RaiseLayoutPropertiesChanged();
    }

    internal void HandleFloatingWindowClosedByUser(ReviewAnalysisCardViewModel slot)
    {
        if (isDisposing || slot.IsClosed || !slot.IsFloating) return;
        if (!slot.IsBottomPreferred && RightDockedAnalysisSlots.Count >= MaxRightDockedPanels)
        {
            // Keep workspace uncluttered by closing excess floating window if dock is full.
            _ = CloseAnalysisAsync(slot.AlgorithmId);
            return;
        }
        slot.IsFloating = false;
        RebuildDockCollections();
        SetActiveSlot(slot);
        RaiseLayoutPropertiesChanged();
    }

    internal void DockFloatingSlotDirect(ReviewAnalysisCardViewModel slot)
    {
        if (isDisposing || slot.IsClosed || !slot.IsFloating) return;
        slot.IsFloating = false;
        if (!slot.IsBottomPreferred)
        {
            foreach (var right in OpenAnalysisSlots.Where(item => !item.IsBottomPreferred))
                right.IsMaximized = false;
        }
        RebuildDockCollections();
        SetActiveSlot(slot);
        RaiseLayoutPropertiesChanged();
    }

    internal void ReportAnalysisCommandError(Exception exception) => ReportCommandError(exception);

    private void SetActiveSlot(ReviewAnalysisCardViewModel? slot)
    {
        if (ReferenceEquals(activeAnalysisSlot, slot))
        {
            RaiseAnalysisPropertiesChanged();
            return;
        }
        if (activeAnalysisSlot is not null) activeAnalysisSlot.IsFocused = false;
        activeAnalysisSlot = slot;
        if (activeAnalysisSlot is not null) activeAnalysisSlot.IsFocused = true;
        RaiseAnalysisPropertiesChanged();
    }

    private void RebuildDockCollections()
    {
        SyncCollection(RightDockedAnalysisSlots, OpenAnalysisSlots.Where(s => !s.IsFloating && !s.IsBottomPreferred));
        SyncCollection(BottomDockedAnalysisSlots, OpenAnalysisSlots.Where(s => !s.IsFloating && s.IsBottomPreferred));
        SyncCollection(FloatingAnalysisSlots, OpenAnalysisSlots.Where(s => s.IsFloating));
        RaiseLayoutPropertiesChanged();
    }

    private static void SyncCollection(
        ObservableCollection<ReviewAnalysisCardViewModel> target,
        IEnumerable<ReviewAnalysisCardViewModel> desired)
    {
        var list = desired.ToList();
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!list.Contains(target[i])) target.RemoveAt(i);
        }
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            var existingIndex = target.IndexOf(item);
            if (existingIndex == i) continue;
            if (existingIndex >= 0) target.Move(existingIndex, i);
            else target.Insert(i, item);
        }
    }

    private void NotifyOpenSlotsPositionChanged()
    {
        foreach (var slot in OpenAnalysisSlots) slot.RaisePositionChanged();
    }

    private void RaiseLayoutPropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsRightAnalysisDockOpen));
        RaisePropertyChanged(nameof(IsBottomAnalysisDockOpen));
        RaisePropertyChanged(nameof(RightDockedPanelCount));
        RaisePropertyChanged(nameof(MaximizedRightAnalysisSlot));
        RaisePropertyChanged(nameof(IsAnyRightPanelMaximized));
        RaisePropertyChanged(nameof(IsRbpSlotOpen));
        RaisePropertyChanged(nameof(IsPsdSlotOpen));
        RaisePropertyChanged(nameof(IsSpectralValuesSlotOpen));
        RaisePropertyChanged(nameof(IsCustomFrequencySlotOpen));
        RaisePropertyChanged(nameof(IsStftSlotOpen));
        RaisePropertyChanged(nameof(IsFaaSlotOpen));
        RaisePropertyChanged(nameof(IsIapfSlotOpen));
        RaisePropertyChanged(nameof(IsThetaBetaSlotOpen));
        RaisePropertyChanged(nameof(IsBrainbeatSlotOpen));
        RaisePropertyChanged(nameof(IsIndividualizedBrainbeatSlotOpen));
        RaisePropertyChanged(nameof(IsTnkSlotOpen));
    }
}
