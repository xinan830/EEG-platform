using System.Diagnostics;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Per-slot cursor binding, topomap projection, and playback fan-out.</summary>
public sealed partial class RecordingReviewViewModel
{
    public async Task AttachAnalysisAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (activeAnalysisSlot is { } slot)
        {
            await DetachSlotAsync(slot);
            cancellationToken.ThrowIfCancellationRequested();
            var binding = CreateSlotBinding(slot, slot.Coordinator);
            binding.AttachRun(runId);
            await LoadAttachedSlotAsync(slot, binding, cancellationToken);
            return;
        }
        await OpenOrAttachLegacyRunAsync(runId, cancellationToken);
    }

    public async Task AttachOnDemandAnalysisAsync(AnalysisRunRequest request,
        CancellationToken cancellationToken = default)
    {
        if (activeAnalysisSlot is null)
            throw new InvalidOperationException("当前回溯页未打开任何分析面板。");
        await AttachOnDemandSlotAsync(activeAnalysisSlot, request, cancellationToken);
    }

    internal void AttachBindingForTest(ReviewAnalysisCoordinator coordinator, ReviewAnalysisBinding binding)
    {
        var slot = new ReviewAnalysisCardViewModel(this, coordinator.AlgorithmId, coordinator.AlgorithmDisplayName,
            ReviewAnalysisPanelKind.Rbp, null, coordinator, PositionSeconds)
        {
            Binding = binding,
        };
        OpenAnalysisSlots.Add(slot);
        RebuildDockCollections();
        SetActiveSlot(slot);
    }

    private async Task OpenOrAttachLegacyRunAsync(string runId, CancellationToken cancellationToken)
    {
        var fallback = analysisCoordinators.Values.FirstOrDefault()
            ?? throw new InvalidOperationException("当前回溯页未配置分析 provider。");
        var slot = CreateSlot(fallback.AlgorithmId);
        OpenAnalysisSlots.Add(slot);
        RebuildDockCollections();
        SetActiveSlot(slot);
        var binding = CreateSlotBinding(slot, slot.Coordinator);
        binding.AttachRun(runId);
        await LoadAttachedSlotAsync(slot, binding, cancellationToken);
    }

    private async Task AttachOnDemandSlotAsync(ReviewAnalysisCardViewModel slot, AnalysisRunRequest request,
        CancellationToken cancellationToken)
    {
        var coordinator = slot.Coordinator;
        if (!coordinator.SupportsOnDemandWindows)
            throw new InvalidOperationException("当前回溯页未启用按需动态窗口。");
        var algorithm = await coordinator.GetAlgorithmAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var binding = CreateSlotBinding(slot, coordinator);
        binding.AttachOnDemand(request, algorithm);
        await LoadAttachedSlotAsync(slot, binding, cancellationToken);
    }

    private ReviewAnalysisBinding CreateSlotBinding(ReviewAnalysisCardViewModel slot, ReviewAnalysisCoordinator coordinator)
    {
        var binding = new ReviewAnalysisBinding(coordinator, analysisRecording?.Id ?? recordingId,
            samplingRateHz, checked((long)Math.Round(DurationSeconds * samplingRateHz,
                MidpointRounding.AwayFromZero)), analysisRecording);
        slot.Binding = binding;
        binding.PropertyChanged += (_, e) => OnSlotBindingChanged(slot, binding, e);
        return binding;
    }

    private async Task LoadAttachedSlotAsync(ReviewAnalysisCardViewModel slot, ReviewAnalysisBinding binding,
        CancellationToken cancellationToken)
    {
        slot.IsStale = false;
        if (ReferenceEquals(activeAnalysisSlot, slot)) RaiseAnalysisPropertiesChanged();
        try { await binding.LoadAtSecondsAsync(PositionSeconds, cancellationToken); }
        catch
        {
            if (ReferenceEquals(binding, slot.Binding)) await DetachSlotAsync(slot);
            throw;
        }
        binding.SetPlaybackActive(playback.IsPlaying && !isTimelineDragging, playback.Snapshot.Generation);
        if (ReferenceEquals(activeAnalysisSlot, slot)) RaiseAnalysisPropertiesChanged();
    }

    public async ValueTask DetachAnalysisAsync()
    {
        var slots = OpenAnalysisSlots.ToArray();
        await Task.WhenAll(slots.Select(DetachSlotAsync));
    }

    private async Task DetachSlotAsync(ReviewAnalysisCardViewModel slot)
    {
        if (slot.IsSpectralInspection) await DetachSpectralSlotAsync(slot);
        if (slot.AlgorithmId == "tnk_biofeedback") UnsubscribeTnkPlaybackEvents(slot);

        Interlocked.Increment(ref slot.TopomapRequestRevision);
        slot.TopomapCancellation?.Cancel();
        slot.TopomapCancellation?.Dispose();
        slot.TopomapCancellation = null;
        if (slot.AlgorithmId == "rbp") RbpAnalysis.ClearTopomap();
        if (slot.AlgorithmId == "psd") PsdAnalysis.ClearTopomap();

        var binding = slot.Binding;
        if (binding is null) return;
        slot.Binding = null;
        slot.ApplyPresentation(null);
        slot.RaiseWindowStateChanged();
        if (ReferenceEquals(activeAnalysisSlot, slot)) RaiseAnalysisPropertiesChanged();
        await binding.DisposeAsync();
    }

    public Task LoadAnalysisAtCurrentPositionAsync() =>
        Task.WhenAll(OpenAnalysisSlots.Select(slot =>
            slot.IsSpectralInspection
                ? RequestSpectralWindowAsync(slot, PositionSeconds)
                : slot.Binding?.LoadAtSecondsAsync(PositionSeconds) ?? Task.CompletedTask));

    private void OnSlotBindingChanged(ReviewAnalysisCardViewModel slot, ReviewAnalysisBinding sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, slot.Binding)) return;
        if (e.PropertyName == nameof(ReviewAnalysisBinding.CurrentWindow))
        {
            slot.ApplyPresentation(slot.CurrentWindow);
            if (slot.AlgorithmId == "rbp" && RbpAnalysis.IsTopomapSelected)
                _ = RefreshRbpTopomapAsync(slot);
            if (slot.AlgorithmId == "psd" && PsdAnalysis.IsTopomapSelected)
                _ = RefreshPsdTopomapAsync(slot);
        }
        if (e.PropertyName == nameof(ReviewAnalysisBinding.StatusText))
        {
            slot.AnalysisPreparationText = slot.CurrentWindow is null ? slot.AnalysisStatusText : "";
            RaiseActiveSlotPreparationIfSame(slot);
        }
        slot.RaiseWindowStateChanged();
        if (ReferenceEquals(activeAnalysisSlot, slot)) RaiseAnalysisPropertiesChanged();
    }

    private async Task RefreshRbpTopomapAsync(ReviewAnalysisCardViewModel slot)
    {
        slot.TopomapCancellation?.Cancel();
        slot.TopomapCancellation?.Dispose();
        var requestRevision = Interlocked.Increment(ref slot.TopomapRequestRevision);
        slot.TopomapCancellation = new CancellationTokenSource();
        var token = slot.TopomapCancellation.Token;
        var coordinator = slot.Coordinator;
        var window = slot.CurrentWindow;
        var provider = coordinator.GetCapability<IReviewTopomapProvider>();
        if (window is null || provider is null)
        {
            RbpAnalysis.ClearTopomap();
            return;
        }
        try
        {
            var loadTimer = Stopwatch.StartNew();
            var fields = string.IsNullOrEmpty(RbpAnalysis.SelectedTopomapBandId)
                ? new[] { "delta", "theta", "alpha", "beta", "gamma" }
                    .Select(band => $"rbp.{RbpAnalysis.SelectedTopomapPowerType}.{band}").ToArray()
                : new[] { $"rbp.{RbpAnalysis.SelectedTopomapPowerType}.{RbpAnalysis.SelectedTopomapBandId}" };
            var result = await provider.LoadTopomapAsync(window, token, fields);
            if (!token.IsCancellationRequested && requestRevision == Volatile.Read(ref slot.TopomapRequestRevision) &&
                ReferenceEquals(window, slot.CurrentWindow) && RbpAnalysis.ApplyTopomapIfAvailable(result))
            {
                var bindTimer = Stopwatch.StartNew();
                if (DesktopLog.PerformanceDiagnosticsEnabled)
                {
                    var gridCells = result?.Fields.Sum(field =>
                        (long)(field.Grid?.Width ?? 0) * (field.Grid?.Height ?? 0)) ?? 0;
                    DesktopLog.Info(
                        $"review_topomap_perf stage=bind" +
                        $" window_id={result?.RunId ?? "none"}" +
                        $" cursor_sample={window.CursorSample}" +
                        $" fields={result?.Fields.Count ?? 0}" +
                        $" grid_cells={gridCells}" +
                        $" load_ms={loadTimer.Elapsed.TotalMilliseconds:0.0}" +
                        $" bind_ms={bindTimer.Elapsed.TotalMilliseconds:0.0}");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            RbpAnalysis.SetStatus($"地形图读取失败：{exception.Message}");
            RbpAnalysis.ClearTopomap();
        }
    }

    private void OnRbpTopomapPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (GetOpenSlot("rbp") is not { } rbpSlot) return;
        if (e.PropertyName is nameof(RbpReviewAnalysisViewModel.SelectedChart) or
                nameof(RbpReviewAnalysisViewModel.SelectedTopomapPowerType) or
                nameof(RbpReviewAnalysisViewModel.SelectedTopomapBandId))
        {
            if (RbpAnalysis.IsTopomapSelected) _ = RefreshRbpTopomapAsync(rbpSlot);
            else
            {
                Interlocked.Increment(ref rbpSlot.TopomapRequestRevision);
                rbpSlot.TopomapCancellation?.Cancel();
            }
        }
    }

    private async Task RefreshPsdTopomapAsync(ReviewAnalysisCardViewModel slot)
    {
        slot.TopomapCancellation?.Cancel();
        slot.TopomapCancellation?.Dispose();
        var requestRevision = Interlocked.Increment(ref slot.TopomapRequestRevision);
        slot.TopomapCancellation = new CancellationTokenSource();
        var token = slot.TopomapCancellation.Token;
        var coordinator = slot.Coordinator;
        var window = slot.CurrentWindow;
        var provider = coordinator.GetCapability<IReviewTopomapProvider>();
        if (window is null || provider is null)
        {
            PsdAnalysis.ClearTopomap();
            return;
        }
        try
        {
            var fieldIds = PsdAnalysis.SelectedTopomapFieldIds;
            var result = await provider.LoadTopomapAsync(window, token, fieldIds);
            if (!token.IsCancellationRequested && requestRevision == Volatile.Read(ref slot.TopomapRequestRevision) &&
                ReferenceEquals(window, slot.CurrentWindow))
                PsdAnalysis.ApplyTopomap(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            PsdAnalysis.SetStatus($"PSD 地形图读取失败：{exception.Message}");
            PsdAnalysis.ClearTopomap();
        }
    }

    private void CancelTopomapRequest(ReviewAnalysisCardViewModel slot)
    {
        Interlocked.Increment(ref slot.TopomapRequestRevision);
        slot.TopomapCancellation?.Cancel();
        if (slot.AlgorithmId == "psd") PsdAnalysis.ClearTopomap();
    }

    private void RaiseAnalysisPropertiesChanged()
    {
        RaisePropertyChanged(nameof(ActiveAnalysisSlot));
        RaisePropertyChanged(nameof(CurrentAnalysisWindow));
        RaisePropertyChanged(nameof(TargetAnalysisCursorSample));
        RaisePropertyChanged(nameof(DisplayedAnalysisCursorSample));
        RaisePropertyChanged(nameof(AnalysisWindowState));
        RaisePropertyChanged(nameof(IsAnalysisTargetDisplayed));
        RaisePropertyChanged(nameof(IsAnalysisLoading));
        RaisePropertyChanged(nameof(AnalysisStatusText));
        RaisePropertyChanged(nameof(IsAnalysisAttached));
        RaisePropertyChanged(nameof(AnalysisPreparationText));
        RaisePropertyChanged(nameof(AnalysisPanelStartSeconds));
        RaisePropertyChanged(nameof(ActiveAnalysisPanel));
        RaisePropertyChanged(nameof(ActiveAnalysisPanelKind));
        RaisePropertyChanged(nameof(IsRbpAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsPsdAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsStftAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsSpectralValuesPanelOpen));
        RaisePropertyChanged(nameof(IsCustomFrequencyInspectionPanelOpen));
        RaisePropertyChanged(nameof(IsFaaAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsIapfAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsThetaBetaAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsBrainbeatAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsTnkAnalysisPanelOpen));
        RaisePropertyChanged(nameof(ActiveScalarParameters));
        RaisePropertyChanged(nameof(ActiveScalarAnalysis));
        RaisePropertyChanged(nameof(HasSecondaryScalarChannel));
        RaisePropertyChanged(nameof(AnalysisPanelTitle));
    }
}
