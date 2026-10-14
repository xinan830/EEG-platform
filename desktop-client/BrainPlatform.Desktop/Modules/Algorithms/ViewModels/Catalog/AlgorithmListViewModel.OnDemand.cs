using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel
{
    private DynamicWindowTargetCoordinator? windowTargets;
    private AnalysisRunRequest? windowRequest;
    private IAlgorithmRunResultHandler? windowHandler;
    private DynamicWindowResponse? displayedWindow;
    private CancellationTokenSource? windowWait;
    private long windowRevision;
    private bool windowLoading;
    internal Task CurrentWindowLoad { get; private set; } = Task.CompletedTask;
    public bool IsOnDemandAnalysis => windowRequest is not null;
    public DynamicWindowResponse? DisplayedAnalysisWindow => displayedWindow;
    internal TimeRange? WindowRequestedRange => windowRequest is { } request &&
        request.Config.TryGetProperty("start_s", out var start) &&
        request.Config.TryGetProperty("end_s", out var end)
        ? new TimeRange(start.GetDouble(), end.GetDouble()) : null;

    private async Task BeginWindowAnalysisAsync(AnalysisRunRequest request,
        IAlgorithmRunResultHandler handler, double initialCursor)
    {
        windowTargets ??= new DynamicWindowTargetCoordinator(client);
        windowRequest = request;
        windowHandler = handler;
        DynamicPreviewCursorSeconds = Math.Clamp(initialCursor, 0, DynamicPreviewDurationSeconds);
        RaiseDynamicPreviewProperties();
        RaisePropertyChanged(nameof(IsOnDemandAnalysis));
        RequestWindowAtCursor();
        await CurrentWindowLoad;
    }

    private void RequestWindowAtCursor()
    {
        if (windowRequest is not { } request || windowHandler is not { } handler ||
            RegisteredRecording is not { } recording || windowTargets is null) return;
        if (recording.SamplingRateHz is not > 0 || recording.DurationSeconds is not > 0)
            throw new InvalidOperationException("记录缺少有效采样率或时长，不能映射按需游标。");
        var samplingRate = recording.SamplingRateHz.Value;
        if (!double.IsFinite(samplingRate) || samplingRate != Math.Truncate(samplingRate))
            throw new InvalidOperationException("当前共享时间映射要求整数采样率，不能静默截断采样率。");
        windowWait?.Cancel();
        windowWait = new CancellationTokenSource();
        var revision = ++windowRevision;
        var cursor = RecordingTimeMapper.ToCursorSample(DynamicPreviewCursorSeconds,
            checked((int)samplingRate),
            checked((long)Math.Round(recording.DurationSeconds.Value * samplingRate)));
        var target = new DynamicWindowRequest(request.RecordingId, handler.AlgorithmId,
            null, null, request.Config, cursor, RequestId: Guid.NewGuid().ToString("N"));
        CurrentWindowLoad = LoadWindowTargetAsync(target, handler, revision, windowWait);
    }

    private async Task LoadWindowTargetAsync(DynamicWindowRequest target,
        IAlgorithmRunResultHandler handler, long revision, CancellationTokenSource waiter)
    {
        windowLoading = true;
        RunStatusText = "正在读取当前目标的按需窗口；保留上次已显示结果。";
        RaisePropertyChanged(nameof(IsRunActive));
        try
        {
            var progress = new Progress<DynamicWindowResponse>(status =>
            {
                if (revision != windowRevision || waiter.IsCancellationRequested || !windowLoading) return;
                RunStatusText = $"按需窗口：{WindowStateLabel(status.State)}；保留上次已显示结果。";
            });
            var response = await windowTargets!.LoadAsync(target, waiter.Token, progress);
            if (revision != windowRevision || waiter.IsCancellationRequested) return;
            if (response.Identity is not { } identity ||
                !identity.TryGetProperty("recording_id", out var source) || source.GetString() != target.RecordingId)
                throw new InvalidOperationException("按需窗口源身份不匹配，已拒绝绑定。");
            handler.ApplyWindowResult(this, response);
            displayedWindow = response;
            DynamicWindowRows.Clear();
            DynamicPreviewRows.Clear();
            if (response.Range is { } actual)
            {
                var row = new DynamicWindowRow(actual.StartSeconds, actual.EndSeconds,
                    WindowStateLabel(response.State), response.Quality ?? "unknown",
                    response.Failure is { } failure ? $"{failure.Code}：{failure.Message}" : "");
                DynamicWindowRows.Add(row);
                DynamicPreviewRows.Add(row);
            }
            RunStatusText = $"按需窗口：{WindowStateLabel(response.State)}";
            ResultSummaryText = response.Failure?.Message ?? $"窗口 {response.WindowId}：{WindowStateLabel(response.State)}";
            ProvenanceText = JsonSerializer.Serialize(new { response.SessionId, response.WindowId, response.Identity,
                response.Range, response.Result?.Provenance });
            StructuredPreviewText = $"窗口：{response.WindowId}；状态：{response.State}；质量：{response.Quality}";
            RaisePropertyChanged(nameof(DisplayedAnalysisWindow));
            RaiseDynamicPreviewProperties();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (revision != windowRevision) return;
            RunStatusText = error is AlgorithmApiException api
                ? $"按需读取失败：{api.Code}：{api.Message}" : $"按需读取失败：{error.Message}";
        }
        finally
        {
            if (revision == windowRevision)
            {
                windowLoading = false;
                windowWait = null;
                RaisePropertyChanged(nameof(IsRunActive));
            }
            waiter.Dispose();
        }
    }

    public async Task ReleaseWindowAnalysisAsync()
    {
        StopDynamicPreviewTimer();
        windowRevision++;
        windowWait?.Cancel();
        windowWait = null;
        windowLoading = false;
        if (windowTargets is not null) await windowTargets.ResetAsync();
        RaisePropertyChanged(nameof(IsRunActive));
    }

    public void ResumeWindowAnalysis() { if (IsOnDemandAnalysis) RequestWindowAtCursor(); }

    private void ClearWindowAnalysis()
    {
        _ = ReleaseWindowAnalysisAsync();
        windowRequest = null;
        windowHandler = null;
        displayedWindow = null;
        psdPreview.Clear();
        stftPreview.Clear();
        RaisePropertyChanged(nameof(IsOnDemandAnalysis));
        RaisePropertyChanged(nameof(DisplayedAnalysisWindow));
    }

    internal static string WindowStateLabel(string state) => state switch
    {
        "ready" => "可用", "insufficient_data" => "数据不足", "rejected" => "质量拒绝",
        "unavailable" => "不可用", "cancelled" => "已取消", "failed" => "计算失败",
        "queued" => "等待计算", "preparing_source" => "准备连续预处理源",
        "computing_window" => "计算窗口", _ => state
    };
}
