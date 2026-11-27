using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Multi-panel algorithm lifecycle; each open panel owns an isolated coordinator and binding.</summary>
public sealed partial class RecordingReviewViewModel
{
    private readonly SemaphoreSlim recordingRegistrationGate = new(1, 1);
    private bool hasUnresolvedAnalysisMontage;
    private bool loadingAnalysisParameters;
    private readonly LatestDelayedTask analysisUpdater = new();

    internal async Task OpenAnalysisAsync(string algorithmId)
    {
        if (algorithmId is SpectralValuesPanelId or CustomFrequencyInspectionPanelId
            ? !analysisCoordinators.ContainsKey("psd") || !analysisCoordinators.ContainsKey("rbp")
            : !analysisCoordinators.ContainsKey(algorithmId) || !analysisPanels.ContainsKey(algorithmId))
            throw new InvalidOperationException($"回放页未配置 {algorithmId} 算法。");

        if (GetOpenSlot(algorithmId) is { } existing)
        {
            FocusAnalysisSlot(algorithmId);
            if (existing.IsStale || !existing.IsAnalysisAttached)
            {
                existing.AnalysisPanelStartSeconds = PositionSeconds;
                existing.IsStale = false;
                await PrepareSlotAsync(existing, analysisLifetime.Token);
            }
            return;
        }

        var slot = CreateSlot(algorithmId);
        if (!slot.IsBottomPreferred && RightDockedAnalysisSlots.Count >= MaxRightDockedPanels)
        {
            slot.IsFloating = true;
            notifications?.PublishInformation($"右侧停靠区最多同屏展示 {MaxRightDockedPanels} 个算法，已将「{slot.DisplayName}」以独立窗口打开。");
        }
        else if (!slot.IsBottomPreferred)
        {
            foreach (var right in OpenAnalysisSlots.Where(item => !item.IsBottomPreferred))
                right.IsMaximized = false;
        }

        OpenAnalysisSlots.Add(slot);
        RebuildDockCollections();
        SetActiveSlot(slot);
        fallbackAnalysisPanelStartSeconds = slot.AnalysisPanelStartSeconds;
        await PrepareSlotAsync(slot, analysisLifetime.Token);
    }

    internal async Task CloseAnalysisAsync()
    {
        var slots = OpenAnalysisSlots.ToArray();
        if (slots.Length == 0)
        {
            SetActiveSlot(null);
            fallbackAnalysisPreparationText = "算法面板已关闭。";
            RaisePropertyChanged(nameof(AnalysisPreparationText));
            return;
        }
        foreach (var slot in slots) MarkSlotClosed(slot);
        OpenAnalysisSlots.Clear();
        RebuildDockCollections();
        SetActiveSlot(null);
        await Task.WhenAll(slots.Select(CloseSlotRuntimeAsync));
        if (!IsAnalysisPanelOpen)
        {
            fallbackAnalysisPreparationText = "算法面板已关闭。";
            RaisePropertyChanged(nameof(AnalysisPreparationText));
        }
    }

    public async Task CloseAnalysisAsync(string algorithmId)
    {
        if (GetOpenSlot(algorithmId) is not { } slot) return;
        MarkSlotClosed(slot);
        OpenAnalysisSlots.Remove(slot);
        RebuildDockCollections();
        if (ReferenceEquals(activeAnalysisSlot, slot))
            SetActiveSlot(OpenAnalysisSlots.LastOrDefault());
        await CloseSlotRuntimeAsync(slot);
        if (!IsAnalysisPanelOpen)
        {
            fallbackAnalysisPreparationText = "算法面板已关闭。";
            RaisePropertyChanged(nameof(AnalysisPreparationText));
        }
    }

    private ReviewAnalysisCardViewModel CreateSlot(string algorithmId)
    {
        var sourceId = algorithmId is SpectralValuesPanelId or CustomFrequencyInspectionPanelId ? "psd" : algorithmId;
        var coordinator = CreateSlotCoordinator(sourceId);
        if (algorithmId == SpectralValuesPanelId)
        {
            return new ReviewAnalysisCardViewModel(this, algorithmId, "频段数值核查",
                ReviewAnalysisPanelKind.SpectralValues, null, coordinator, PositionSeconds);
        }
        if (algorithmId == CustomFrequencyInspectionPanelId)
        {
            return new ReviewAnalysisCardViewModel(this, algorithmId, "自定义频段",
                ReviewAnalysisPanelKind.CustomFrequencyInspection, null, coordinator, PositionSeconds);
        }
        var registration = analysisPanels[algorithmId];
        return new ReviewAnalysisCardViewModel(this, algorithmId, registration.DisplayName,
            registration.PanelKind, registration, coordinator, PositionSeconds);
    }

    private ReviewAnalysisCoordinator CreateSlotCoordinator(string sourceAlgorithmId)
    {
        var primary = analysisCoordinators[sourceAlgorithmId];
        var inUse = OpenAnalysisSlots.Any(slot => ReferenceEquals(slot.Coordinator, primary));
        if (!inUse) return primary;
        if (analysisAlgorithmClient is not null && analysisPanels.TryGetValue(sourceAlgorithmId, out var registration))
            return registration.CreateCoordinator(analysisAlgorithmClient);
        return primary.CloneForSlot();
    }

    private void MarkSlotClosed(ReviewAnalysisCardViewModel slot)
    {
        slot.IsClosed = true;
        slot.PreparationCancellation?.Cancel();
        slot.ConfigurationRevision++;
        slot.Binding?.DetachRun();
        slot.SpectralRbpBinding?.DetachRun();
        if (slot.AlgorithmId == SpectralValuesPanelId) ClearSpectralValuesSlot(slot);
        if (slot.AlgorithmId == CustomFrequencyInspectionPanelId) ClearCustomFrequencySlot(slot);
        slot.ApplyPresentation(null);
    }

    private async Task CloseSlotRuntimeAsync(ReviewAnalysisCardViewModel slot)
    {
        await slot.PreparationGate.WaitAsync();
        try { await DetachSlotAsync(slot); }
        finally { slot.PreparationGate.Release(); }
    }

    private async Task<RegisteredRecording> EnsureRegisteredAnalysisRecordingAsync(
        ReviewAnalysisCoordinator coordinator, CancellationToken token)
    {
        if (analysisRecording is { } existing) return existing;
        await recordingRegistrationGate.WaitAsync(token);
        try
        {
            if (analysisRecording is { } doubleChecked) return doubleChecked;
            if (analysisRecordingDirectory is null)
                throw new InvalidOperationException("当前回溯记录缺少可用于算法分析的来源身份。");
            analysisRecording = await coordinator.RegisterAsync(analysisRecordingDirectory, token,
                montageSelection.AvailableMontages.ToArray());
            return analysisRecording;
        }
        finally { recordingRegistrationGate.Release(); }
    }

    private async Task PrepareSlotAsync(ReviewAnalysisCardViewModel slot, CancellationToken cancellationToken)
    {
        slot.PreparationCancellation?.Cancel();
        using var preparation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, analysisLifetime.Token);
        slot.PreparationCancellation = preparation;
        var token = preparation.Token;
        var revision = slot.ConfigurationRevision;
        try
        {
            await slot.PreparationGate.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                if (slot.IsClosed || revision != slot.ConfigurationRevision) return;
                await DetachSlotAsync(slot);
                token.ThrowIfCancellationRequested();
                if (slot.IsClosed || revision != slot.ConfigurationRevision) return;

                if (slot.AlgorithmId == "tnk_biofeedback") SubscribeTnkPlaybackEvents(slot);
                slot.AnalysisPreparationText = $"正在准备 {slot.Coordinator.AlgorithmDisplayName}…";
                RaiseActiveSlotPreparationIfSame(slot);

                var recorded = await EnsureRegisteredAnalysisRecordingAsync(slot.Coordinator, token);
                token.ThrowIfCancellationRequested();
                var algorithm = await slot.Coordinator.GetAlgorithmAsync(token);
                var rbpAlgorithm = slot.IsSpectralInspection
                    ? await analysisCoordinators["rbp"].GetAlgorithmAsync(token) : null;
                token.ThrowIfCancellationRequested();

                var selection = montageSelection.Capture();
                var montage = ResolveAnalysisMontage(recorded, selection.SelectedMontage);
                hasUnresolvedAnalysisMontage = selection.SelectedMontage is not null && montage is null;
                if (hasUnresolvedAnalysisMontage)
                    throw new ReviewInputMappingException("montage_snapshot_unavailable",
                        "后端没有当前导联快照，不能沿用旧导联结果。", "montage");
                var inputs = await ResolvePreprocessingAnalysisInputsAsync(recorded, montage, algorithm, token);
                token.ThrowIfCancellationRequested();
                if (slot.IsClosed || revision != slot.ConfigurationRevision || selection.Revision != montageSelection.Revision)
                    return;

                montage = inputs.Montage;
                var panel = analysisPanels[slot.SourceAlgorithmId];
                loadingAnalysisParameters = true;
                try
                {
                    panel.InitializeForSelection(inputs.Recording, algorithm, selection, montage);
                    if (rbpAlgorithm is not null)
                        analysisPanels["rbp"].InitializeForSelection(inputs.Recording, rbpAlgorithm, selection, montage);
                }
                finally { loadingAnalysisParameters = false; }

                var duration = recorded.DurationSeconds ?? DurationSeconds;
                var rate = recorded.SamplingRateHz ?? samplingRateHz;
                var config = panel.BuildConfigurationForSelection(duration, rate, selection, montage);
                config = AttachActivePreprocessing(config, algorithm);
                if (slot.IsClosed || selection.Revision != montageSelection.Revision) return;

                var request = new AnalysisRunRequest(recorded.Id, "official_algorithm", config);
                if (rbpAlgorithm is not null)
                {
                    AttachSpectralRbp(slot, rbpAlgorithm, selection, montage);
                    var binding = CreateSlotBinding(slot, slot.Coordinator);
                    binding.AttachOnDemand(request, algorithm);
                    slot.IsStale = false;
                    slot.RaiseWindowStateChanged();
                    RaiseAnalysisPropertiesChanged();
                    await RequestSpectralWindowAsync(slot, PositionSeconds, cancellationToken: token);
                }
                else
                {
                    await AttachOnDemandSlotAsync(slot, request, token);
                }

                token.ThrowIfCancellationRequested();
                slot.AnalysisPreparationText = slot.CurrentWindow is null ? slot.AnalysisStatusText : "";
                RaiseActiveSlotPreparationIfSame(slot);
            }
            finally { slot.PreparationGate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested || slot.IsClosed || revision != slot.ConfigurationRevision) return;
            if (slot.AlgorithmId == SpectralValuesPanelId) SpectralValues.Clear($"数值准备失败：{exception.Message}");
            if (slot.AlgorithmId == CustomFrequencyInspectionPanelId) CustomFrequencyInspection.Clear($"数值准备失败：{exception.Message}");
            slot.AnalysisPreparationText = $"算法准备失败：{exception.Message}";
            RaiseActiveSlotPreparationIfSame(slot);
            throw;
        }
        finally
        {
            if (ReferenceEquals(slot.PreparationCancellation, preparation)) slot.PreparationCancellation = null;
        }
    }

    private void RaiseActiveSlotPreparationIfSame(ReviewAnalysisCardViewModel slot)
    {
        if (ReferenceEquals(activeAnalysisSlot, slot))
            RaisePropertyChanged(nameof(AnalysisPreparationText));
    }

    private void OnAnalysisConfigurationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (loadingAnalysisParameters || OpenAnalysisSlots.Count == 0) return;
        if (e.PropertyName is not (nameof(ReviewRbpParameters.Channel) or nameof(ReviewRbpParameters.Channels) or
            nameof(ReviewScalarParameters.Channel) or nameof(ReviewScalarParameters.SecondaryChannel) or
            nameof(ReviewScalarParameters.FrequencyMode) or
            nameof(ReviewStftParameters.Channel) or nameof(ReviewPsdParameters.Channels) or
            nameof(ReviewTnkParameters.RoleMapping)))
            return;

        var matchingSlots = OpenAnalysisSlots.Where(slot =>
            ReferenceEquals(slot.Registration?.Configuration, sender) ||
            (slot.IsSpectralInspection && (ReferenceEquals(sender, PsdParameters) || ReferenceEquals(sender, RbpParameters))))
            .ToArray();
        if (matchingSlots.Length > 0) InvalidateSlots(matchingSlots);
    }

    private void UpdateAlgorithmMontageInputs(RegisteredRecording recording,
        WorkspaceMontageSelectionSnapshot<MontageProfile> previousSelection,
        WorkspaceMontageSelectionSnapshot<MontageProfile> selectedSelection)
    {
        var previous = ResolveAnalysisMontage(recording, previousSelection.SelectedMontage);
        var selected = ResolveAnalysisMontage(recording, selectedSelection.SelectedMontage);
        hasUnresolvedAnalysisMontage = selectedSelection.SelectedMontage is not null && selected is null;
        if (hasUnresolvedAnalysisMontage) return;

        loadingAnalysisParameters = true;
        try
        {
            foreach (var panel in analysisPanels.Values)
                panel.RefreshForSelection(recording, previousSelection, selectedSelection, previous, selected);
            PsdAnalysis.SetChannels(PsdParameters.Channels);
            RbpAnalysis.InitializeChannels(RbpParameters.ResolveAnalysisChannels(selected));
            if (IsSpectralValuesSlotOpen || IsCustomFrequencySlotOpen)
            {
                SpectralValues.InitializeChannels(PsdParameters.Channels);
                CustomFrequencyInspection.InitializeChannels(PsdParameters.Channels);
            }
        }
        finally { loadingAnalysisParameters = false; }
    }

    private static HistoricalMontageDescriptor? ResolveAnalysisMontage(RegisteredRecording recording, MontageProfile? selected) =>
        selected is null ? null : recording.MontageCatalogue?.FirstOrDefault(item =>
            string.Equals(item.Id, selected.Id, StringComparison.Ordinal) ||
            string.Equals(item.SourceProfileId, selected.Id, StringComparison.Ordinal));

    private void InvalidateAnalysisConfiguration() => InvalidateSlots(OpenAnalysisSlots.ToArray());

    private void InvalidateSlots(IReadOnlyList<ReviewAnalysisCardViewModel> slots)
    {
        if (slots.Count == 0) return;
        foreach (var slot in slots)
        {
            slot.PreparationCancellation?.Cancel();
            if (slot.AlgorithmId == SpectralValuesPanelId) InvalidateSpectralSlot(slot);
            if (slot.AlgorithmId == CustomFrequencyInspectionPanelId) InvalidateSpectralSlot(slot);
            slot.Binding?.DetachRun();
            slot.ConfigurationRevision++;
            slot.IsStale = true;
            slot.ApplyPresentation(null);
            slot.AnalysisPreparationText = "参数已改变，等待自动分析。";
        }
        RaiseAnalysisPropertiesChanged();
        if (Preprocessing.AreScientificInputsBlocked)
        {
            var blocked = Preprocessing.HasUnsavedChannelIdentities ? "通道身份尚未保存，分析已暂停。" : Preprocessing.ReferenceStatusText;
            foreach (var slot in slots) slot.AnalysisPreparationText = blocked;
            RaisePropertyChanged(nameof(AnalysisPreparationText));
            return;
        }
        if (analysisLifetime.IsCancellationRequested) return;
        var snapshots = slots.Select(slot => (Slot: slot, Revision: slot.ConfigurationRevision)).ToArray();
        analysisUpdater.Schedule(TimeSpan.FromMilliseconds(300), async token =>
        {
            var pending = snapshots
                .Where(entry => !entry.Slot.IsClosed && entry.Revision == entry.Slot.ConfigurationRevision)
                .Select(async entry =>
                {
                    try { await PrepareSlotAsync(entry.Slot, token); }
                    catch (Exception exception) when (!token.IsCancellationRequested) { ReportCommandError(exception); }
                });
            await Task.WhenAll(pending);
        });
    }
}
