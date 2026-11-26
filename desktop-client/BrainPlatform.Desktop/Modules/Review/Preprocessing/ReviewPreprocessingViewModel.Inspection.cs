using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Preprocessing;

/// <summary>Owns bounded inspections and the pending-to-confirmed ICA transaction.</summary>
public sealed partial class ReviewPreprocessingViewModel
{
    private CancellationTokenSource? inspectionCts;
    private long inspectionGeneration;

    private void InvalidateArtifactInspection()
    {
        ArtifactSegments.Clear();
        ArtifactMarkers = [];
        CleanRatioSummary = "检测输入已变更，请重新扫描坏段";
    }

    private readonly record struct InspectionOperation(long Generation, long Revision, CancellationToken Token);

    private InspectionOperation BeginInspection()
    {
        CancelInspection();
        inspectionCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        IsBusy = true;
        return new(inspectionGeneration, configurationRevision, inspectionCts.Token);
    }

    private void CancelInspection()
    {
        inspectionGeneration++;
        inspectionCts?.Cancel();
        inspectionCts?.Dispose();
        inspectionCts = null;
        IsBusy = false;
    }

    private void EnsureInspectionCurrent(InspectionOperation operation)
    {
        operation.Token.ThrowIfCancellationRequested();
        if (operation.Generation != inspectionGeneration || operation.Revision != configurationRevision)
            throw new OperationCanceledException("预处理输入已变化，请重新检查。");
    }

    private void EndInspection(InspectionOperation operation)
    {
        if (operation.Generation == inspectionGeneration) IsBusy = false;
    }

    public async Task InspectBadChannelsAsync()
    {
        var client = RequireClient();
        var operation = BeginInspection();
        var token = operation.Token;
        StatusMessage = "正在扫描原始未投影 EEG 通道的坏导指标…";
        try
        {
            var (recId, _, duration) = await ensureBackendRecordingAsync(token);
            var response = await client.InspectBadChannelsAsync(recId, 0, Math.Min(Math.Max(duration, 4), 180), token);
            EnsureInspectionCurrent(operation);
            var previousPolicy = BadChannelPolicy;
            var previousSelection = BadChannelRows.ToDictionary(row => row.Channel, row => row.IsSelected, StringComparer.OrdinalIgnoreCase);
            var previousBads = SelectedBadChannels.ToHashSet(StringComparer.OrdinalIgnoreCase);
            BadChannelRows.Clear();
            foreach (var item in response.Channels)
                BadChannelRows.Add(new BadChannelRowViewModel(item, OnBadChannelSelectionChanged,
                    previousSelection.TryGetValue(item.Channel, out var selected) ? selected : null));

            if (previousSelection.Count == 0 && SelectedBadChannels.Count > 0 && BadChannelPolicy == "none")
            {
                var allCanInterp = BadChannelRows.Where(c => c.IsSelected).All(c => c.CanInterpolate);
                badChannelPolicy = allCanInterp ? "interpolate" : "exclude";
                RaisePropertyChanged(nameof(BadChannelPolicy));
            }
            StatusMessage = response.RecommendedBadChannels.Count == 0
                ? $"坏导体检完成（{response.StartSeconds:0.###}–{response.EndSeconds:0.###} s，{response.Channels.Count} 导）：未发现坏导。"
                : $"坏导体检完成（{response.StartSeconds:0.###}–{response.EndSeconds:0.###} s）：建议处理 {response.RecommendedBadChannels.Count} 路坏导（{string.Join(", ", response.RecommendedBadChannels)}）。";
            var inputsChanged = previousPolicy != BadChannelPolicy ||
                (BadChannelPolicy != "none" && !previousBads.SetEquals(SelectedBadChannels));
            if (inputsChanged)
            {
                InvalidateArtifactInspection();
                InvalidateIcaFingerprint("坏导输入已变更，需重新审查 ICA 成分");
                NotifyConfigAndVisuals();
            }
            else
            {
                RaisePropertyChanged(nameof(SelectedBadChannels));
                RaisePipelineNodeProperties();
                VisualStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) when (token.IsCancellationRequested) { }
        finally { EndInspection(operation); }
    }

    public async Task InspectArtifactSegmentsAsync()
    {
        var payload = BuildPayload(allowArtifactRemoval: true);
        var client = RequireClient();
        var operation = BeginInspection();
        var token = operation.Token;
        StatusMessage = "正在准备整段科学信号并读取统一坏段报告（1–50 Hz + 50 Hz 陷波）…";
        try
        {
            var (recId, montageId, duration) = await ensureBackendRecordingAsync(token);
            var response = await client.InspectArtifactSegmentsAsync(
                recId, 0, Math.Min(Math.Max(duration, 4), 180), montageId,
                PeakToPeakUv, MuscleZScoreThreshold, payload.BadChannels.Channels, payload.BadChannels.Policy,
                payload.ArtifactRemoval.EogChannels, payload.ArtifactRemoval.EcgChannels, token);
            EnsureInspectionCurrent(operation);
            if (response.Segments.Count > 0 && ArtifactPolicy == "none")
            {
                ArtifactPolicy = "reject";
                var effective = BuildPayload(allowArtifactRemoval: true).ArtifactRemoval;
                if (!payload.ArtifactRemoval.EogChannels.SequenceEqual(effective.EogChannels) ||
                    !(payload.ArtifactRemoval.EcgChannels ?? []).SequenceEqual(effective.EcgChannels ?? []))
                {
                    StatusMessage = "坏段拒收已启用，原 ICA 确认已失效且通道范围已改变，请重新扫描坏段并审查 ICA。";
                    return;
                }
            }
            ArtifactSegments.Clear();
            foreach (var seg in response.Segments)
                ArtifactSegments.Add(new ArtifactSegmentRowViewModel(seg));

            ArtifactMarkers = ArtifactSegments.Select(s => s.ToMarker()).ToArray();
            CleanRatioSummary = $"整段统一检测 · 当前列出 {response.StartSeconds:0.###}–{response.EndSeconds:0.###} s · 干净率 {response.CleanRatio * 100:0.0}% · 检出 {response.Segments.Count} 段坏段（{ReviewPreprocessingProjection.FormatMuscleStatus(response.MuscleCheckStatus)}）";
            StatusMessage = response.Segments.Count == 0
                ? $"坏段扫描完成：{response.StartSeconds:0.###}–{response.EndSeconds:0.###} s 范围内未检出超限坏段。"
                : $"坏段扫描完成（{response.StartSeconds:0.###}–{response.EndSeconds:0.###} s）：检出 {response.Segments.Count} 段，点击列表可一键跳转定位。";
            RaisePipelineNodeProperties();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) when (token.IsCancellationRequested) { }
        finally { EndInspection(operation); }
    }

    public async Task<IcaInspectionResponse> InspectIcaForDialogAsync()
    {
        ValidateReferenceSelection(forIca: true);
        var client = RequireClient();
        pendingIca = null;
        var operation = BeginInspection();
        var token = operation.Token;
        StatusMessage = "正在整段连续拟合 Extended Infomax ICA 并执行 ICLabel 分类与 2D 热力图生成…";
        try
        {
            var (recId, montageId, _) = await ensureBackendRecordingAsync(token);
            var response = await client.InspectIcaComponentsAsync(
                recId, montageId, lowHz: 1.0, highHz: 50.0, notchHz: 50.0,
                badChannels: SelectedBadChannels, badChannelPolicy: BadChannelPolicy,
                artifactPolicy: ArtifactPolicy, peakToPeakUv: PeakToPeakUv,
                muscleZScoreThreshold: MuscleZScoreThreshold,
                allowFrontalEogProxy: SelectedEogChannels.Count == 0 && AllowFrontalEogProxy,
                icaComponentCount: IcaComponentCount, icaEogThreshold: 0.7,
                eogChannels: SelectedEogChannels, ecgChannels: SelectedEcgChannels,
                cancellationToken: token);

            EnsureInspectionCurrent(operation);
            pendingIca = response;
            pendingIcaRevision = configurationRevision;
            IcaComponents.Clear();
            var preselected = (confirmedIcaExcluded is not null && string.Equals(icaFitFingerprint, response.IcaFitFingerprint, StringComparison.Ordinal))
                ? confirmedIcaExcluded.ToHashSet()
                : response.RecommendedExcludeComponents.ToHashSet();
            foreach (var comp in response.Components)
                IcaComponents.Add(new IcaComponentCardViewModel(comp, preselected.Contains(comp.ComponentIndex)));

            StatusMessage = $"ICA 拟合完成（共 {response.ComponentCount} 个成分，建议剔除：{ReviewPreprocessingProjection.FormatComponentList(response.RecommendedExcludeComponents)}）。";
            return response;
        }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        finally { EndInspection(operation); }
    }

    public void CancelIcaReview()
    {
        pendingIca = null;
        IcaComponents.Clear();
        StatusMessage = "已取消本次 ICA 审查，保留原有已确认配置。";
    }

    public void ConfirmIcaSelection(IReadOnlyList<int> excludedIndices, string? fitFingerprint)
    {
        if (pendingIca is not { CanApplyToContinuous: true } pending ||
            pendingIcaRevision != configurationRevision || string.IsNullOrWhiteSpace(fitFingerprint) ||
            !string.Equals(pending.IcaFitFingerprint, fitFingerprint, StringComparison.Ordinal) ||
            excludedIndices.Any(index => !pending.Components.Any(c => c.ComponentIndex == index)))
            throw new InvalidOperationException("ICA 选择与当前已审查模型不匹配，请重新审查。");
        icaFitFingerprint = fitFingerprint;
        if (SelectedEogChannels.Count > 0 || SelectedEcgChannels.Count > 0) InvalidateArtifactInspection();
        confirmedIcaExcluded = excludedIndices.Distinct().OrderBy(i => i).ToArray();
        icaInvalidatedPendingReview = false;
        removalMethod = "ica_infomax";
        RaisePropertyChanged(nameof(RemovalMethod));
        RaisePropertyChanged(nameof(IcaFitFingerprint));
        RaisePropertyChanged(nameof(ConfirmedIcaExcluded));
        var proxyNote = pending.EogChannelSource == "frontal_eeg_proxy" ? "（前额代理）" : "";
        IcaSummaryText = $"已确认剔除 {ReviewPreprocessingProjection.FormatComponentList(confirmedIcaExcluded)}{proxyNote} · 指纹 {fitFingerprint?[..Math.Min(8, fitFingerprint.Length)]}";
        StatusMessage = $"ICA 成分选择已锁定（{IcaSummaryText}），后续 PSD/RBP 等算法将直接复用模型。";
        pendingIca = null;
        NotifyConfigAndVisuals();
    }

}
