using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>One active algorithm panel; preparation is serialized and cancelled on selection changes.</summary>
public sealed partial class RecordingReviewViewModel
{
    private readonly SemaphoreSlim analysisPreparationGate = new(1, 1);
    private CancellationTokenSource? analysisPreparation;
    private bool hasUnresolvedAnalysisMontage;
    private bool loadingAnalysisParameters;
    private readonly LatestDelayedTask analysisUpdater = new();

    internal async Task OpenAnalysisAsync(string algorithmId)
    {
        if (algorithmId == SpectralValuesPanelId
            ? !analysisCoordinators.ContainsKey("psd") || !analysisCoordinators.ContainsKey("rbp")
            : !analysisCoordinators.ContainsKey(algorithmId) || !analysisPanels.ContainsKey(algorithmId))
            throw new InvalidOperationException($"回放页未配置 {algorithmId} 算法。");
        SetActiveAnalysis(algorithmId);
        await PrepareAnalysisAsync(algorithmId, analysisLifetime.Token);
    }

    internal async Task CloseAnalysisAsync()
    {
        SetActiveAnalysis(null);
        var revision = analysisConfigurationRevision;
        await analysisPreparationGate.WaitAsync();
        try
        {
            if (activeAnalysisId is null && revision == analysisConfigurationRevision) await DetachAnalysisAsync();
        }
        finally { analysisPreparationGate.Release(); }
        if (!IsAnalysisPanelOpen) AnalysisPreparationText = "算法面板已关闭。";
    }

    private void SetActiveAnalysis(string? algorithmId)
    {
        analysisPreparation?.Cancel();
        InvalidateSpectralValues();
        analysisBinding?.DetachRun();
        analysisConfigurationRevision++;
        activeAnalysisId = algorithmId;
        analysisPanelStartSeconds = PositionSeconds;
        foreach (var panel in analysisPanels.Values) panel.Apply(null, PositionSeconds);
        RaisePropertyChanged(nameof(AnalysisPanelStartSeconds));
        RaisePropertyChanged(nameof(IsAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsSpectralValuesPanelOpen));
        RaisePropertyChanged(nameof(IsRbpAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsPsdAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsStftAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsTnkAnalysisPanelOpen));
        RaisePropertyChanged(nameof(AnalysisPanelTitle));
        RaisePropertyChanged(nameof(ActiveAnalysisPanel));
        RaisePropertyChanged(nameof(ActiveAnalysisPanelKind));
        RaisePropertyChanged(nameof(ActiveScalarParameters));
        RaisePropertyChanged(nameof(ActiveScalarAnalysis));
        RaisePropertyChanged(nameof(HasSecondaryScalarChannel));
    }

    private async Task PrepareAnalysisAsync(string algorithmId, CancellationToken cancellationToken)
    {
        analysisPreparation?.Cancel();
        using var preparation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, analysisLifetime.Token);
        analysisPreparation = preparation;
        var token = preparation.Token;
        var revision = analysisConfigurationRevision;
        try
        {
            await analysisPreparationGate.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                if (activeAnalysisId != algorithmId || revision != analysisConfigurationRevision) return;
                await DetachAnalysisAsync();
                token.ThrowIfCancellationRequested();
                var sourceId = algorithmId == SpectralValuesPanelId ? "psd" : algorithmId;
                analysisCoordinator = analysisCoordinators[sourceId];
                SubscribeTnkPlaybackEvents(analysisCoordinator);
                AnalysisPreparationText = $"正在准备 {analysisCoordinator.AlgorithmDisplayName}…";
                if (analysisRecordingDirectory is null)
                    throw new InvalidOperationException("当前回溯记录不支持算法注册。");
                analysisRecording ??= await analysisCoordinator.RegisterAsync(analysisRecordingDirectory, token,
                    CompatibleViewingMontages.ToArray());
                token.ThrowIfCancellationRequested();
                var algorithm = await analysisCoordinator.GetAlgorithmAsync(token);
                var rbpAlgorithm = IsSpectralValuesPanelOpen
                    ? await analysisCoordinators["rbp"].GetAlgorithmAsync(token) : null;
                token.ThrowIfCancellationRequested();
                var selection = montageSelection.Capture();
                var montage = ResolveAnalysisMontage(analysisRecording, selection.SelectedMontage);
                hasUnresolvedAnalysisMontage = selection.SelectedMontage is not null && montage is null;
                if (hasUnresolvedAnalysisMontage)
                    throw new ReviewInputMappingException("montage_snapshot_unavailable",
                        "后端没有当前导联快照，不能沿用旧导联结果。", "montage");
                var panel = analysisPanels[sourceId];
                loadingAnalysisParameters = true;
                try
                {
                    panel.InitializeForSelection(analysisRecording, algorithm, selection, montage);
                    if (rbpAlgorithm is not null)
                        analysisPanels["rbp"].InitializeForSelection(analysisRecording, rbpAlgorithm, selection, montage);
                }
                finally { loadingAnalysisParameters = false; }
                var duration = analysisRecording.DurationSeconds ?? DurationSeconds;
                var rate = analysisRecording.SamplingRateHz ?? samplingRateHz;
                var config = panel.BuildConfigurationForSelection(duration, rate, selection, montage);
                if (selection.Revision != montageSelection.Revision) return;
                if (rbpAlgorithm is not null) AttachSpectralRbp(rbpAlgorithm, selection, montage);
                var primary = AttachOnDemandAnalysisAsync(new AnalysisRunRequest(analysisRecording.Id, "official_algorithm", config), token);
                await Task.WhenAll(primary, spectralRbpBinding?.LoadAtSecondsAsync(PositionSeconds, token) ?? Task.CompletedTask);
                token.ThrowIfCancellationRequested();
                AnalysisPreparationText = CurrentAnalysisWindow is null ? AnalysisStatusText : "";
            }
            finally { analysisPreparationGate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested || revision != analysisConfigurationRevision) return;
            if (IsSpectralValuesPanelOpen) SpectralValues.Clear($"数值准备失败：{exception.Message}");
            AnalysisPreparationText = $"算法准备失败：{exception.Message}";
            throw;
        }
        finally
        {
            if (ReferenceEquals(analysisPreparation, preparation)) analysisPreparation = null;
        }
    }

    private void OnAnalysisConfigurationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (loadingAnalysisParameters || activeAnalysisId is not { } activeId ||
            !analysisPanels.TryGetValue(activeId, out var panel) || !ReferenceEquals(sender, panel.Configuration)) return;
        if (e.PropertyName is nameof(ReviewRbpParameters.Channel) or nameof(ReviewRbpParameters.Channels) or
            nameof(ReviewScalarParameters.Channel) or nameof(ReviewScalarParameters.SecondaryChannel) or
            nameof(ReviewScalarParameters.FrequencyMode) or
            nameof(ReviewStftParameters.Channel) or nameof(ReviewPsdParameters.Channels) or
            nameof(ReviewTnkParameters.RoleMapping))
            InvalidateAnalysisConfiguration();
    }

    private void UpdateAlgorithmMontageInputs(RegisteredRecording recording,
        WorkspaceMontageSelectionSnapshot<MontageProfile> previousSelection, WorkspaceMontageSelectionSnapshot<MontageProfile> selectedSelection)
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
            if (IsSpectralValuesPanelOpen) SpectralValues.InitializeChannels(PsdParameters.Channels);
        }
        finally { loadingAnalysisParameters = false; }
    }

    private static HistoricalMontageDescriptor? ResolveAnalysisMontage(RegisteredRecording recording, MontageProfile? selected) =>
        selected is null ? null : recording.MontageCatalogue?.FirstOrDefault(item =>
            string.Equals(item.Id, selected.Id, StringComparison.Ordinal) ||
            string.Equals(item.SourceProfileId, selected.Id, StringComparison.Ordinal));

    private void InvalidateAnalysisConfiguration()
    {
        analysisPreparation?.Cancel();
        InvalidateSpectralValues();
        analysisBinding?.DetachRun();
        analysisConfigurationRevision++;
        isAnalysisStale = true;
        foreach (var panel in analysisPanels.Values) panel.Apply(null, PositionSeconds);
        AnalysisPreparationText = "参数已改变，等待自动分析。";
        RaiseAnalysisPropertiesChanged();
        if (activeAnalysisId is not { } algorithmId || analysisLifetime.IsCancellationRequested) return;
        var revision = analysisConfigurationRevision;
        analysisUpdater.Schedule(TimeSpan.FromMilliseconds(300), async token =>
        {
            if (activeAnalysisId != algorithmId || revision != analysisConfigurationRevision || analysisLifetime.IsCancellationRequested) return;
            try { await PrepareAnalysisAsync(algorithmId, token); }
            catch (Exception exception) when (!token.IsCancellationRequested) { ReportCommandError(exception); }
        });
    }
}
