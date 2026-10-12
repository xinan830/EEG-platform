using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Modules.Review.Scheduling;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Review analysis preparation and configuration revision boundaries; no playback clock or science.</summary>
public sealed partial class RecordingReviewViewModel
{
    private Task? analysisParameterLoadTask;
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
            var request = new AnalysisRunRequest(registered.Id, "official_algorithm",
                PsdParameters.BuildConfig(registered.DurationSeconds ?? DurationSeconds,
                    registered.SamplingRateHz ?? samplingRateHz,
                    new ReviewAnalysisSettingsSnapshot(HighPassHz, LowPassHz, NotchHz, AnalysisSettings.WindowSeconds)));
            revision = analysisConfigurationRevision;
            await DetachAnalysisAsync();
            var run = await EnsurePsdAnalysisAsync(request, status =>
            {
                if (revision != analysisConfigurationRevision) return;
                PsdPreparationText = status.Status switch
                {
                    "queued" => "PSD 排队中…",
                    "running" when status.ProgressTotal is > 0 => $"PSD 分析中：{status.ProgressText}",
                    "running" => "PSD 分析中…",
                    _ => PsdPreparationText,
                };
            }, cancellationToken, revision);
            if (cancellationToken.IsCancellationRequested || revision != analysisConfigurationRevision) return;
            PsdPreparationText = run.Status == "gate_failed"
                ? "PSD 质量门未通过，已显示失败原因。"
                : CurrentAnalysisWindow is null
                    ? analysisBinding?.StatusText ?? "当前游标暂无可用 PSD 窗口。"
                    : "PSD 已准备，正在跟随回溯游标。";
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

        var registered = await analysisCoordinator.RegisterAsync(analysisRecordingDirectory, analysisLifetime.Token);
        var algorithm = await analysisCoordinator.GetAlgorithmAsync(analysisLifetime.Token);
        PsdParameters.Initialize(registered, algorithm);
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
        if (e.PropertyName is nameof(ReviewPsdParameters.Channel) or nameof(ReviewAnalysisSettings.WindowSeconds))
            InvalidateAnalysisConfiguration();
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
