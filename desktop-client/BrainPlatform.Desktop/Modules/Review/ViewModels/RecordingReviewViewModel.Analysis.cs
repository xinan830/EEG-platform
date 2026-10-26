using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Modules.Review.Scheduling;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Review analysis preparation and configuration revision boundaries; no playback clock or science.</summary>
public sealed partial class RecordingReviewViewModel
{
    private Task? analysisParameterLoadTask;
    private bool hasUnresolvedAnalysisMontage;
    private readonly LatestDelayedTask analysisUpdater = new();
    public Task PrepareDefaultPsdAsync() => PreparePsdAsync(analysisLifetime.Token);

    private async Task PreparePsdAsync(CancellationToken cancellationToken)
    {
        if (analysisCoordinator is null || analysisRecordingDirectory is null)
            throw new InvalidOperationException("当前回溯记录不支持算法注册。");
        PsdPreparationText = "正在注册记录并加载 PSD 配置…";
        long? revision = null;
        try
        {
            await EnsurePsdParametersLoadedAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var registered = analysisRecording ?? throw new InvalidOperationException("PSD 记录注册未完成。");
            if (hasUnresolvedAnalysisMontage)
                throw new InvalidOperationException("当前回溯导联没有对应的后端分析快照，已停止计算以避免沿用旧导联结果。");
            var request = new AnalysisRunRequest(registered.Id, "official_algorithm",
                PsdParameters.BuildConfig(registered.DurationSeconds ?? DurationSeconds,
                    registered.SamplingRateHz ?? samplingRateHz,
                    new ReviewAnalysisSettingsSnapshot(HighPassHz, LowPassHz, NotchHz, AnalysisSettings.WindowSeconds),
                    PsdParameters.Montage));
            revision = analysisConfigurationRevision;
            await DetachAnalysisAsync();
            if (!analysisCoordinator.SupportsOnDemandWindows)
                throw new NotSupportedException("PSD 尚未通过回溯按需适配验收；此页面不会回退到完整 Run 计算。");
            await AttachOnDemandAnalysisAsync(request, cancellationToken);
            if (cancellationToken.IsCancellationRequested || revision != analysisConfigurationRevision) return;
            PsdPreparationText = CurrentAnalysisWindow is null
                ? AnalysisStatusText
                : "PSD 按需窗口已准备，正在跟随回溯游标。";
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested || analysisLifetime.IsCancellationRequested || (revision is not null && revision != analysisConfigurationRevision)) return;
            PsdPreparationText = $"PSD 准备失败：{exception.Message}";
            throw;
        }
    }

    private async Task OpenPsdAnalysisAsync()
    {
        await EnsurePsdParametersLoadedAsync();
        IsPsdAnalysisPanelOpen = true;
        await PrepareDefaultPsdAsync();
    }

    private Task EnsurePsdParametersLoadedAsync()
    {
        if (analysisRecording is not null && PsdParameters.Channels.Count > 0) return Task.CompletedTask;
        if (analysisParameterLoadTask is { IsCompleted: false }) return analysisParameterLoadTask;
        return analysisParameterLoadTask = LoadPsdParametersAsync();
    }

    private async Task LoadPsdParametersAsync()
    {
        if (analysisCoordinator is null || analysisRecordingDirectory is null)
            throw new InvalidOperationException("当前回溯记录不支持算法注册。");
        if (analysisRecording is not null && PsdParameters.Channels.Count > 0) return;

        var registered = await analysisCoordinator.RegisterAsync(analysisRecordingDirectory, analysisLifetime.Token,
            CompatibleViewingMontages.ToArray());
        var algorithm = await analysisCoordinator.GetAlgorithmAsync(analysisLifetime.Token);
        var selectedMontage = ResolveAnalysisMontage(registered, CurrentViewingMontage);
        hasUnresolvedAnalysisMontage = CurrentViewingMontage is not null && selectedMontage is null;
        if (hasUnresolvedAnalysisMontage)
            throw new InvalidOperationException("后端没有登记当前回溯导联配置，不能安全地计算 PSD。");
        PsdParameters.Initialize(registered, algorithm, selectedMontage);
        AnalysisSettings.Initialize(algorithm.DynamicPolicy);
        analysisRecording = registered;
        PsdPreparationText = "PSD 参数已加载。";
    }

    public async Task<AnalysisRunResponse> EnsurePsdAnalysisAsync(
        AnalysisRunRequest request,
        Action<AnalysisRunResponse>? onStatusChanged = null,
        CancellationToken cancellationToken = default,
        long? expectedConfigurationRevision = null)
    {
        if (analysisCoordinator is null)
            throw new InvalidOperationException("当前回溯页未配置分析 provider。");
        if (!string.Equals(request.AnalysisType, "official_algorithm", StringComparison.Ordinal) ||
            !request.Config.TryGetProperty("algorithm_id", out var algorithm) ||
            !string.Equals(algorithm.GetString(), "psd", StringComparison.Ordinal))
        {
            throw new ArgumentException("当前回溯入口只接受 PSD 官方算法配置。", nameof(request));
        }

        var revision = expectedConfigurationRevision ?? analysisConfigurationRevision;
        var run = await analysisCoordinator.EnsureRunAsync(request, onStatusChanged, cancellationToken);
        if (run.Status is not ("completed" or "gate_failed"))
            throw new InvalidOperationException($"PSD 分析未完成：{run.Status}。");
        if (!analysisLifetime.IsCancellationRequested &&
            revision == analysisConfigurationRevision)
            await AttachAnalysisAsync(run.RunId, cancellationToken);
        return run;
    }

    private void OnAnalysisConfigurationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReviewPsdParameters.Channel) or nameof(ReviewPsdParameters.Montage) or
            nameof(ReviewAnalysisSettings.WindowSeconds))
            InvalidateAnalysisConfiguration();
    }

    private static HistoricalMontageDescriptor? ResolveAnalysisMontage(
        RegisteredRecording recording, MontageProfile? selected)
    {
        if (selected is null) return null;
        return recording.MontageCatalogue?.FirstOrDefault(item =>
            string.Equals(item.Id, selected.Id, StringComparison.Ordinal) ||
            string.Equals(item.SourceProfileId, selected.Id, StringComparison.Ordinal));
    }

    private void InvalidateAnalysisConfiguration()
    {
        analysisConfigurationRevision++;
        isAnalysisStale = true;
        PsdPreparationText = "参数已改变，等待自动分析。";
        RaiseAnalysisPropertiesChanged();
        if (!IsPsdAnalysisPanelOpen || analysisLifetime.IsCancellationRequested) return;
        analysisUpdater.Schedule(TimeSpan.FromMilliseconds(300), async token =>
        {
            if (!IsPsdAnalysisPanelOpen || analysisLifetime.IsCancellationRequested) return;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, analysisLifetime.Token);
            try { await PreparePsdAsync(linked.Token); }
            catch (Exception exception) when (!linked.IsCancellationRequested) { ReportCommandError(exception); }
        });
    }
}
