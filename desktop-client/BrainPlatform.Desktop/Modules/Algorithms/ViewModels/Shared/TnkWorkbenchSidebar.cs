using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed partial class TnkDetailViewModel
{
    private CancellationTokenSource? sidebarLifetime;
    private CancellationTokenSource? summaryRequest;
    private string summaryTarget = "";
    private TnkWorkbenchSummary? scoreHistory;
    private long displayedScoreSecond = -1;
    private string scoreStateStatus = "完成全量动态分析后显示状态时长";
    public IReadOnlyList<TnkScoreState> ScoreStates { get; private set; } = [];
    public string ScoreStateStatus { get => scoreStateStatus; private set => SetProperty(ref scoreStateStatus, value); }
    private void ActivateSidebar()
    {
        sidebarLifetime?.Cancel(); sidebarLifetime?.Dispose(); sidebarLifetime = new();
        summaryTarget = ""; scoreHistory = null; displayedScoreSecond = -1;
        RefreshScoreStates();
    }
    private void DeactivateSidebar()
    {
        sidebarLifetime?.Cancel(); sidebarLifetime?.Dispose(); sidebarLifetime = null;
        summaryRequest?.Cancel(); summaryRequest?.Dispose(); summaryRequest = null;
        summaryTarget = ""; scoreHistory = null; ScoreStates = []; RaisePropertyChanged(nameof(ScoreStates));
    }
    private void RefreshScoreStates()
    {
        if (!workbenchActive || sidebarLifetime is null) return;
        var run = CompletedBackgroundFullRun ?? (!Catalog.IsOnDemandAnalysis ? Catalog.LastRun : null);
        if (run is not { Status: "completed" } || run.RecordingId != Catalog.RegisteredRecording?.Id || run.RunId == withheldRunId)
        {
            summaryRequest?.Cancel(); summaryTarget = ""; scoreHistory = null;
            if (Catalog.IsOnDemandAnalysis)
            {
                ApplyOnDemandScoreStates();
                return;
            }
            ScoreStates = []; RaisePropertyChanged(nameof(ScoreStates));
            ScoreStateStatus = Catalog.IsOnDemandAnalysis
                ? OnDemandStateSummary()
                : "完成完整动态 Run 后显示全程状态时长";
            return;
        }
        if (scoreHistory is { } history && history.RunId == run.RunId)
        { ApplyScoreFrame(history); return; }
        var target = run.RunId;
        if (summaryTarget == target) return;
        summaryTarget = target; summaryRequest?.Cancel(); summaryRequest?.Dispose();
        summaryRequest = CancellationTokenSource.CreateLinkedTokenSource(sidebarLifetime.Token);
        _ = LoadScoreStatesAsync(run.RunId, run.RecordingId, RecordingDuration, summaryRequest.Token);
    }

    private string OnDemandStateSummary()
    {
        if (Catalog.DisplayedAnalysisWindow is not { } window)
            return "按需分析：等待当前游标窗口返回；全程状态时长需完整动态 Run。";
        var results = ChildResults;
        var held = results.Count(item => item.FailureCode == "TNK_UPDATE_NOT_DUE");
        var available = results.Count(item => item.FailureCode != "TNK_UPDATE_NOT_DUE" &&
            item.Chart.Series.Any(series => series.Points.LastOrDefault()?.Value.HasValue == true));
        var unavailable = Math.Max(0, results.Count - available - held);
        var time = window.Range?.EndSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) ?? "?";
        var total = ScoreStates.Sum(state => state.DurationSeconds);
        return $"当前窗口 {time} s：{available} 项有数值 · {held} 项未到更新节拍 · {unavailable} 项不可用。已返回窗口累计 {total:0.#} s。";
    }

    private void ApplyOnDemandScoreStates()
    {
        var series = ChildResults.FirstOrDefault(child => child.AlgorithmId == "tnk_composite_meditation")?.Chart.Series.FirstOrDefault();
        ScoreStates = TnkOnDemandScoreStateProjection.Project(series, WaveformPosition);
        RaisePropertyChanged(nameof(ScoreStates));
        ScoreStateStatus = "按需分析暂显示已返回窗口累计 · 缺失窗口不累计";
    }
    private async Task LoadScoreStatesAsync(string runId, string recordingId, double cursor, CancellationToken token)
    {
        try
        {
            var result = await Catalog.WorkbenchClient.GetTnkWorkbenchSummaryAsync(runId, cursor, token);
            if (token.IsCancellationRequested || recordingId != Catalog.RegisteredRecording?.Id || result.RunId != runId || result.RecordingId != recordingId) return;
            scoreHistory = result; displayedScoreSecond = -1; ApplyScoreFrame(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested) ScoreStateStatus = error.Message; }
    }
    private void ApplyScoreFrame(TnkWorkbenchSummary history)
    {
        var second = (long)Math.Floor(WaveformPosition);
        if (displayedScoreSecond == second) return;
        displayedScoreSecond = second;
        var frame = history.Timeline.LastOrDefault(item => item.CursorSeconds <= WaveformPosition);
        ScoreStates = frame?.States ?? []; RaisePropertyChanged(nameof(ScoreStates));
        ScoreStateStatus = $"有效区间 {frame?.ValidDurationSeconds ?? 0:0.#} s · 记录时间统计，缺失窗口不累计";
    }
}
