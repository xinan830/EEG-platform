using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;
using System.Collections.Concurrent;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel
{
    private bool isSubmittingRun;
    private long configurationRevision;
    private readonly ConcurrentDictionary<string, byte> obsoleteRunCancellations = new(StringComparer.Ordinal);

    private async Task RunAsync()
    {
        if (disposed) return;
        if (isSubmittingRun) return;
        isSubmittingRun = true;
        RaisePropertyChanged(nameof(IsRunActive));
        try { await ExecuteRunAsync(); }
        finally
        {
            isSubmittingRun = false;
            RaisePropertyChanged(nameof(IsRunActive));
            if (!disposed && !detailSuspended && automaticRunPending) automaticRunDebounceTimer.Start();
        }
    }

    private async Task ExecuteRunAsync()
    {
        automaticRunPending = false;
        automaticRunDebounceTimer.Stop();
        var algorithm = SelectedAlgorithm ?? throw new InvalidOperationException("请选择一个官方算法。");
        if (!AlgorithmRunConfiguration.IsSupportedAlgorithm(algorithm))
        {
            throw new InvalidOperationException("当前算法不可运行，或未声明可用的分析模式。");
        }
        var resultHandler = AlgorithmRunResultHandlerRegistry.For(algorithm.Id);
        var dynamic = IsDynamicMode;
        if (dynamic && !IsDynamicModeAvailable)
            throw new InvalidOperationException("当前算法未声明动态分析模式。");
        if (RegisteredRecording is null)
        {
            throw new InvalidOperationException("请先注册一条已完成的 WPF 记录。");
        }
        var durationSeconds = RegisteredRecording.DurationSeconds ?? throw new InvalidOperationException("注册记录没有可用的时长。");
        var requestedRange = AlgorithmRunConfiguration.ParseStaticRangeOrThrow(StartSecondsText, EndSecondsText, durationSeconds);
        var runInputs = new AlgorithmRunInputs(requestedRange, SelectedChannel, SelectedF4Channel,
            RegisteredChannels, RegisteredRecording.SamplingRateHz, LowFrequencyText, HighFrequencyText,
            NumeratorLowFrequencyText, NumeratorHighFrequencyText, DenominatorLowFrequencyText,
            DenominatorHighFrequencyText, SelectedNotchFrequency,
            TnkRoleChannels.ToDictionary(role => role.Key, role => role.SelectedChannel, StringComparer.OrdinalIgnoreCase));
        var runParameters = resultHandler.ParseParameters(runInputs);
        var runChannel = runParameters.PrimaryChannel ?? SelectedChannel;
        if (string.IsNullOrWhiteSpace(runChannel) || !RegisteredChannels.Contains(runChannel))
            throw new InvalidOperationException("请选择注册记录返回的有效分析通道。");
        var stepSeconds = dynamic ? AlgorithmRunConfiguration.ParsePositiveSecondsOrThrow(DynamicStepText, "刷新步长") : 0;
        if (dynamic && SelectedDynamicWindowSeconds < algorithm.DynamicPolicy.MinimumWindowSeconds)
            throw new InvalidOperationException($"动态窗口不能小于 {algorithm.DynamicPolicy.MinimumWindowSeconds:0.###} 秒。");
        var preservedDynamicCursorSeconds = dynamic && HasDynamicPreview
            ? DynamicPreviewCursorSeconds
            : (double?)null;
        var config = AlgorithmRunConfiguration.BuildRunConfig(algorithm, runChannel, requestedRange, dynamic ? "dynamic" : "static", runParameters.FrequencyBand, runParameters.RatioBands, runParameters.F4Channel, dynamic ? SelectedDynamicWindowSeconds : null, dynamic ? stepSeconds : null, runParameters.NotchHz ?? 0.0, SelectedMontageProfile, runParameters.RoleChannels);
        AlgorithmParameterValidation.ValidateOrThrow(algorithm, config);
        ClearPreviews();
        // Backend atomically merges active scientific configurations. New attempts
        // deliberately omit a permanent key so cancelled work can be rerun.
        var request = new AnalysisRunRequest(RegisteredRecording.Id, "official_algorithm", config);
        if (dynamic && (IsOnDemandExecutionModeSelected || IsCombinedExecutionModeSelected))
        {
            if (!algorithm.SupportsDeclaredOnDemand || !resultHandler.SupportsOnDemandWindows)
                throw new InvalidOperationException("当前算法未启用按需分析，请选择全量分析。");
            if (IsCombinedExecutionModeSelected && !algorithm.SupportsDeclaredCombined)
                throw new InvalidOperationException("当前算法未声明组合动态分析策略，请选择其他分析方式。");
            var previewRevision = configurationRevision;
            await BeginWindowAnalysisAsync(request, resultHandler, preservedDynamicCursorSeconds ?? 0);
            if (!disposed && !detailSuspended && IsCombinedExecutionModeSelected && configurationRevision == previewRevision &&
                RegisteredRecording?.Id == request.RecordingId && SelectedAlgorithm?.Id == algorithm.Id)
            {
                var fullRequest = request with
                {
                    IdempotencyKey = AlgorithmRunConfiguration.StableFullRunIdempotencyKey(request),
                };
                BeginBackgroundFullRun(fullRequest, algorithm.Id, previewRevision);
            }
            return;
        }
        var requestedRevision = configurationRevision;
        var run = await runCoordinator.CreateRunAndWaitAsync(
            request,
            updatedRun =>
            {
                if (RegisteredRecording?.Id != request.RecordingId || SelectedAlgorithm?.Id != algorithm.Id ||
                    configurationRevision != requestedRevision)
                    return;
                LastRun = updatedRun;
                RunStatusText = $"{algorithm.DisplayNameZh}：{updatedRun.Status}（{updatedRun.RunId}）" +
                    (updatedRun.ProgressTotal is int total ? $"，已完成 {updatedRun.ProgressCompleted}/{total} 窗口" : "");
                RaisePropertyChanged(nameof(RunProgressText));
                RaisePropertyChanged(nameof(IsRunActive));
            },
            CancellationToken.None);
        if (RegisteredRecording?.Id != request.RecordingId || SelectedAlgorithm?.Id != algorithm.Id ||
            configurationRevision != requestedRevision)
            return;
        LastRun = run;
        RaisePropertyChanged(nameof(RunProgressText));
        RaisePropertyChanged(nameof(IsRunActive));
        if (run.Status == "completed")
        {
            RunStatusText += "，结果已由后端保存。";
            ResultSummaryText = AlgorithmResultFormatter.BuildResultSummary(run);
            ProvenanceText = AlgorithmResultFormatter.BuildProvenance(run);
            DynamicSeriesText = dynamic ? AlgorithmResultFormatter.BuildDynamicSeriesSummary(run) : "暂无动态结果。";
            if (dynamic && resultHandler.UsesDynamicScalarSeries)
                SetDynamicSeries(run);
            await resultHandler.ApplyAsync(this, run, dynamic, preservedDynamicCursorSeconds);
        }
        else if (run.Error is not null)
        {
            RunStatusText += $"，{run.Error.Code}：{run.Error.Message}";
            ResultSummaryText = $"结果不可用：{run.Error.Code}。{run.Error.Message}";
            ProvenanceText = AlgorithmResultFormatter.BuildProvenance(run);
            StructuredPreviewText = "结果未完成，无法加载结构化预览。";
        }
        else
        {
            ResultSummaryText = $"结果不可用：运行状态为 {run.Status}。";
            ProvenanceText = AlgorithmResultFormatter.BuildProvenance(run);
            StructuredPreviewText = "结果未完成，无法加载结构化预览。";
        }
    }

    private async Task CancelRunAsync()
    {
        if (IsOnDemandAnalysis)
        {
            await ReleaseWindowAnalysisAsync();
            RunStatusText = "已释放当前页面的按需分析订阅。";
            return;
        }
        if (LastRun is not { Status: "queued" or "running" } run)
            return;
        var updated = await client.CancelRunAsync(run.RunId, CancellationToken.None);
        if (LastRun?.RunId != run.RunId) return;
        LastRun = updated;
        RunStatusText = $"分析已请求取消：{LastRun.Status}";
        RaisePropertyChanged(nameof(IsRunActive));
        RaisePropertyChanged(nameof(RunProgressText));
    }

    private async Task CancelObsoleteRunAsync(AnalysisRunResponse run)
    {
        if (!obsoleteRunCancellations.TryAdd(run.RunId, 0))
            return;

        try
        {
            AnalysisRunResponse updated;
            try
            {
                updated = await client.CancelRunAsync(run.RunId, CancellationToken.None);
            }
            catch (AlgorithmApiException error) when (error.Code == "RUN_NOT_CANCELLABLE")
            {
                updated = await client.GetRunAsync(run.RunId, CancellationToken.None);
            }

            if (!disposed && LastRun?.RunId == run.RunId)
            {
                LastRun = updated;
                RunStatusText = updated.Status is "completed" or "failed" or "cancelled"
                    ? $"过期配置的分析已结束：{updated.Status}"
                    : $"已取消过期配置的分析：{updated.Status}";
                RaisePropertyChanged(nameof(IsRunActive));
                RaisePropertyChanged(nameof(RunProgressText));
            }
        }
        catch (Exception error)
        {
            if (!disposed)
                notifications.PublishError($"过期配置的分析取消失败：{error.Message}");
        }
        finally
        {
            obsoleteRunCancellations.TryRemove(run.RunId, out _);
        }
    }

}
