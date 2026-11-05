using BrainPlatform.Desktop.Modules.Review.Analysis;

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
        if (!analysisCoordinators.ContainsKey(algorithmId) || !analysisPanels.ContainsKey(algorithmId))
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
        analysisBinding?.DetachRun();
        analysisConfigurationRevision++;
        activeAnalysisId = algorithmId;
        analysisPanelStartSeconds = PositionSeconds;
        foreach (var panel in analysisPanels.Values) panel.Apply(null, PositionSeconds);
        RaisePropertyChanged(nameof(AnalysisPanelStartSeconds));
        RaisePropertyChanged(nameof(IsAnalysisPanelOpen));
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
                analysisCoordinator = analysisCoordinators[algorithmId];
                SubscribeTnkPlaybackEvents(analysisCoordinator);
                AnalysisPreparationText = $"正在准备 {analysisCoordinator.AlgorithmDisplayName}…";
                if (analysisRecordingDirectory is null)
                    throw new InvalidOperationException("当前回溯记录不支持算法注册。");
                analysisRecording ??= await analysisCoordinator.RegisterAsync(analysisRecordingDirectory, token,
                    CompatibleViewingMontages.ToArray());
                token.ThrowIfCancellationRequested();
                var algorithm = await analysisCoordinator.GetAlgorithmAsync(token);
                token.ThrowIfCancellationRequested();
                var montage = ResolveAnalysisMontage(analysisRecording, CurrentViewingMontage);
                hasUnresolvedAnalysisMontage = CurrentViewingMontage is not null && montage is null;
                if (hasUnresolvedAnalysisMontage)
                    throw new InvalidOperationException("后端没有当前导联快照，不能沿用旧导联结果。");
                var panel = analysisPanels[algorithmId];
                loadingAnalysisParameters = true;
                try { panel.Initialize(analysisRecording, algorithm, montage); }
                finally { loadingAnalysisParameters = false; }
                var duration = analysisRecording.DurationSeconds ?? DurationSeconds;
                var rate = analysisRecording.SamplingRateHz ?? samplingRateHz;
                var config = panel.BuildConfiguration(duration, rate);
                await AttachOnDemandAnalysisAsync(new AnalysisRunRequest(analysisRecording.Id, "official_algorithm", config), token);
                token.ThrowIfCancellationRequested();
                AnalysisPreparationText = CurrentAnalysisWindow is null ? AnalysisStatusText : "";
            }
            finally { analysisPreparationGate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested || revision != analysisConfigurationRevision) return;
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
        if (e.PropertyName is nameof(ReviewRbpParameters.Channel) or nameof(ReviewRbpParameters.Montage) or
            nameof(ReviewScalarParameters.Channel) or nameof(ReviewScalarParameters.SecondaryChannel) or
            nameof(ReviewScalarParameters.FrequencyMode) or
            nameof(ReviewScalarParameters.Montage) or nameof(ReviewStftParameters.Channel) or
            nameof(ReviewTnkParameters.RoleMapping) or nameof(ReviewTnkParameters.Montage))
            InvalidateAnalysisConfiguration();
    }

    private static HistoricalMontageDescriptor? ResolveAnalysisMontage(RegisteredRecording recording, MontageProfile? selected) =>
        selected is null ? null : recording.MontageCatalogue?.FirstOrDefault(item =>
            string.Equals(item.Id, selected.Id, StringComparison.Ordinal) ||
            string.Equals(item.SourceProfileId, selected.Id, StringComparison.Ordinal));

    private void InvalidateAnalysisConfiguration()
    {
        analysisPreparation?.Cancel();
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
