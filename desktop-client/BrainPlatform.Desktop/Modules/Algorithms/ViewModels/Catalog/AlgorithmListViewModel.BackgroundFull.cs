using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel
{
    private AnalysisRunResponse? backgroundFullRun;
    private AnalysisRunRequest? backgroundFullRequest;
    private string? backgroundFullAlgorithmId;
    private long backgroundFullRevision;
    private Task? backgroundFullRunTask;
    private CancellationTokenSource? backgroundFullObservationCancellation;
    private Exception? backgroundFullObservationError;
    private string? backgroundFullObservationMessage;
    private bool disposed;
    private bool detailSuspended;
    private Task detailReleaseTask = Task.CompletedTask;

    public AnalysisRunResponse? BackgroundFullRun
    {
        get => backgroundFullRun;
        private set => SetProperty(ref backgroundFullRun, value);
    }

    public bool HasBackgroundFullRun => BackgroundFullRun is not null ||
        backgroundFullRequest is not null || backgroundFullObservationError is not null ||
        backgroundFullObservationMessage is not null;

    public bool IsBackgroundFullRunActive =>
        BackgroundFullRun?.Status is "queued" or "running" ||
        (backgroundFullRunTask is not null && backgroundFullObservationError is null &&
         backgroundFullObservationMessage is null);

    public double BackgroundFullRunProgressPercent => BackgroundFullRun?.ProgressPercent ?? 0;
    public string BackgroundFullRunProgressText => BackgroundFullRun?.ProgressText ??
        (backgroundFullObservationError is not null ? "观察失败" : "未启动");

    public string BackgroundFullRunStatusText =>
        backgroundFullObservationMessage is { } message ? message :
        backgroundFullObservationError is { } error
            ? $"后台全量观察失败：{FormatBackgroundError(error)}" :
        BackgroundFullRun switch
        {
            null => "未启动后台全量分析。",
            { Error: { } runError } run => $"后台全量：{run.Status}，{runError.Code}：{runError.Message}",
            { } run => $"后台全量：{run.ProgressText}（{run.RunId}）",
        };

    private void BeginBackgroundFullRun(AnalysisRunRequest request, string algorithmId, long requestedRevision)
    {
        if (disposed || detailSuspended) return;

        if (IsSameBackgroundRequest(request, algorithmId, requestedRevision) &&
            backgroundFullObservationError is null && backgroundFullObservationMessage is null)
        {
            if (BackgroundFullRun?.Status is "queued" or "running" && backgroundFullRunTask is null)
                StartBackgroundObservation(BackgroundFullRun);
            return;
        }

        InvalidateBackgroundFullRunDisplay();
        backgroundFullRequest = request;
        backgroundFullAlgorithmId = algorithmId;
        backgroundFullRevision = requestedRevision;
        StartBackgroundObservation(initialRun: null);
    }

    private bool IsSameBackgroundRequest(
        AnalysisRunRequest request, string algorithmId, long requestedRevision) =>
        backgroundFullRequest is { } current &&
        current.RecordingId == request.RecordingId &&
        current.IdempotencyKey == request.IdempotencyKey &&
        current.Config.GetRawText() == request.Config.GetRawText() &&
        backgroundFullAlgorithmId == algorithmId &&
        backgroundFullRevision == requestedRevision;

    private void StartBackgroundObservation(AnalysisRunResponse? initialRun)
    {
        if (disposed || detailSuspended || backgroundFullRequest is not { } request ||
            backgroundFullAlgorithmId is not { } algorithmId)
            return;

        StopBackgroundObservation();
        backgroundFullObservationError = null;
        backgroundFullObservationMessage = null;
        var owner = new CancellationTokenSource();
        backgroundFullObservationCancellation = owner;
        var task = ObserveBackgroundFullRunAsync(
            request, algorithmId, backgroundFullRevision, initialRun, owner);
        // The fake client and a cache hit can complete synchronously. The
        // observer's finally block may already have cleared this owner before
        // the async method returns, so do not resurrect a completed task.
        backgroundFullRunTask = ReferenceEquals(backgroundFullObservationCancellation, owner)
            ? task : null;
        RaiseBackgroundFullRunProperties();
    }

    private async Task ObserveBackgroundFullRunAsync(
        AnalysisRunRequest request,
        string algorithmId,
        long requestedRevision,
        AnalysisRunResponse? initialRun,
        CancellationTokenSource owner)
    {
        try
        {
            if (!IsCurrentBackgroundOwner(request, algorithmId, requestedRevision, owner)) return;

            var run = initialRun is null
                ? await runCoordinator.CreateRunAndWaitAsync(
                    request,
                    updatedRun => PublishBackgroundFullRun(request, algorithmId, requestedRevision, owner, updatedRun),
                    owner.Token)
                : await runCoordinator.ObserveRunAndWaitAsync(
                    initialRun,
                    updatedRun => PublishBackgroundFullRun(request, algorithmId, requestedRevision, owner, updatedRun),
                    owner.Token);
            PublishBackgroundFullRun(request, algorithmId, requestedRevision, owner, run);
        }
        catch (OperationCanceledException)
        {
            // Cancellation only stops this page's observer. The persisted Run
            // remains available for a later resume or historical lookup.
        }
        catch (Exception error)
        {
            if (!IsCurrentBackgroundOwner(request, algorithmId, requestedRevision, owner)) return;
            backgroundFullObservationError = error;
            backgroundFullObservationMessage = null;
            RaiseBackgroundFullRunProperties();
        }
        finally
        {
            if (ReferenceEquals(backgroundFullObservationCancellation, owner))
            {
                backgroundFullObservationCancellation = null;
                backgroundFullRunTask = null;
                owner.Dispose();
                RaiseBackgroundFullRunProperties();
            }
            else
            {
                owner.Dispose();
            }
        }
    }

    private void PublishBackgroundFullRun(
        AnalysisRunRequest request,
        string algorithmId,
        long requestedRevision,
        CancellationTokenSource owner,
        AnalysisRunResponse run)
    {
        if (!IsCurrentBackgroundOwner(request, algorithmId, requestedRevision, owner)) return;
        FullRunPreviewIntegrity.RequireRun(run, request);
        var wasCompleted = BackgroundFullRun?.Status == "completed";
        BackgroundFullRun = run;
        backgroundFullObservationError = null;
        backgroundFullObservationMessage = null;
        if (!detailSuspended) RaiseBackgroundFullRunProperties();
        if (!detailSuspended && run.Status == "completed" &&
            windowHandler is { SupportsFullRunWindowPreview: true, SupportsFullRunMetricWindowPreview: false })
        {
            // Start loading the immutable full artifact as soon as it is
            // complete. The first cursor move should not pay the structured
            // preview download cost or the interactive throttle interval.
            _ = EnsureFullWindowCacheAsync(run, CancellationToken.None);
        }
        if (!detailSuspended && !wasCompleted && run.Status == "completed" &&
            IsCombinedExecutionModeSelected && IsOnDemandAnalysis)
        {
            // The current cursor may already have been requested through the
            // on-demand worker. Invalidate only that request identity so the
            // next load can read the completed Full artifact instead.
            lastRequestedWindowSample = -1;
            QueueWindowRequest(immediate: true);
        }
    }

    private bool IsCurrentBackgroundOwner(
        AnalysisRunRequest request,
        string algorithmId,
        long requestedRevision,
        CancellationTokenSource owner) =>
        !disposed &&
        ReferenceEquals(backgroundFullRequest, request) &&
        ReferenceEquals(backgroundFullObservationCancellation, owner) &&
        backgroundFullAlgorithmId == algorithmId &&
        backgroundFullRevision == requestedRevision &&
        configurationRevision == requestedRevision &&
        RegisteredRecording?.Id == request.RecordingId &&
        SelectedAlgorithm?.Id == algorithmId;

    private async Task CancelBackgroundFullRunAsync()
    {
        var run = BackgroundFullRun;
        if (run is null)
        {
            if (backgroundFullRequest is null) return;
            StopBackgroundObservation();
            backgroundFullRequest = null;
            backgroundFullObservationMessage = "后台全量分析已取消。";
            RaiseBackgroundFullRunProperties();
            return;
        }

        if (run.Status is not ("queued" or "running")) return;

        // Keep the command safe for a restored view whose persisted Run was
        // loaded before its request snapshot was reconstructed.
        if (backgroundFullRequest is null)
        {
            try
            {
                var updatedOrphan = await client.CancelRunAsync(run.RunId, CancellationToken.None);
                if (BackgroundFullRun?.RunId == run.RunId)
                {
                    BackgroundFullRun = updatedOrphan;
                    RaiseBackgroundFullRunProperties();
                }
            }
            catch (Exception error)
            {
                backgroundFullObservationError = error;
                RaiseBackgroundFullRunProperties();
            }
            return;
        }

        var request = backgroundFullRequest;
        var algorithmId = backgroundFullAlgorithmId;
        StopBackgroundObservation();

        AnalysisRunResponse updated;
        try
        {
            updated = await client.CancelRunAsync(run.RunId, CancellationToken.None);
        }
        catch (Exception error)
        {
            backgroundFullObservationError = error;
            backgroundFullObservationMessage = null;
            RaiseBackgroundFullRunProperties();
            return;
        }

        if (disposed || backgroundFullRequest is not { } currentRequest ||
            !ReferenceEquals(currentRequest, request) || BackgroundFullRun?.RunId != run.RunId)
            return;

        BackgroundFullRun = updated;
        backgroundFullObservationError = null;
        backgroundFullObservationMessage = updated.Status is "queued" or "running"
            ? "已请求取消，等待后端确认。"
            : $"后台全量分析：{updated.ProgressText}";
        RaiseBackgroundFullRunProperties();

        // Cancellation is cooperative. A backend may acknowledge the request
        // before the worker reaches its next cancellation checkpoint.
        if (updated.Status is "queued" or "running" && algorithmId is not null)
        {
            backgroundFullObservationMessage = null;
            StartBackgroundObservation(updated);
        }
    }

    private void StopBackgroundObservation()
    {
        var owner = backgroundFullObservationCancellation;
        backgroundFullObservationCancellation = null;
        backgroundFullRunTask = null;
        owner?.Cancel();
        // The observer owns disposal. This prevents an old observer's finally
        // block from disposing a newer observer's token source.
    }

    private void InvalidateBackgroundFullRunDisplay()
    {
        var hadState = HasBackgroundFullRun;
        StopBackgroundObservation();
        backgroundFullRequest = null;
        backgroundFullAlgorithmId = null;
        backgroundFullRevision = 0;
        backgroundFullObservationError = null;
        backgroundFullObservationMessage = null;
        if (BackgroundFullRun is null)
        {
            if (hadState) RaiseBackgroundFullRunProperties();
            return;
        }
        BackgroundFullRun = null;
        RaiseBackgroundFullRunProperties();
    }

    public async Task SuspendDetailAsync()
    {
        if (disposed) return;
        detailSuspended = true;
        StopBackgroundObservation();
        var release = ReleaseWindowAnalysisAsync();
        detailReleaseTask = release;
        await release;
    }

    public void ResumeDetail()
    {
        if (disposed) return;
        if (!detailSuspended && backgroundFullRunTask is not null) return;
        detailSuspended = false;
        _ = ResumeAfterDetailReleaseAsync();
    }

    private async Task ResumeAfterDetailReleaseAsync()
    {
        try { await detailReleaseTask; }
        catch (Exception error)
        {
            if (!disposed && !detailSuspended)
            {
                backgroundFullObservationError = error;
                RaiseBackgroundFullRunProperties();
            }
            return;
        }

        if (disposed || detailSuspended) return;
        if (BackgroundFullRun?.Status is "queued" or "running")
            StartBackgroundObservation(BackgroundFullRun);
        else if (BackgroundFullRun is null && backgroundFullRequest is not null &&
                 backgroundFullObservationError is null)
            StartBackgroundObservation(initialRun: null);
        RaiseBackgroundFullRunProperties();
        ResumeWindowAnalysis();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        configurationRevision++;
        automaticRunDebounceTimer.Stop();
        dynamicPreviewTimer.Stop();
        StopBackgroundObservation();
        await ReleaseWindowAnalysisAsync();
        backgroundFullRequest = null;
        backgroundFullAlgorithmId = null;
        backgroundFullObservationError = null;
        backgroundFullObservationMessage = null;
    }

    private void RaiseBackgroundFullRunProperties()
    {
        RaisePropertyChanged(nameof(BackgroundFullRun));
        RaisePropertyChanged(nameof(HasBackgroundFullRun));
        RaisePropertyChanged(nameof(IsBackgroundFullRunActive));
        RaisePropertyChanged(nameof(BackgroundFullRunProgressPercent));
        RaisePropertyChanged(nameof(BackgroundFullRunProgressText));
        RaisePropertyChanged(nameof(BackgroundFullRunStatusText));
    }

    private static string FormatBackgroundError(Exception error) => error is AlgorithmApiException api
        ? $"{api.Code}：{api.Message}" : error.Message;
}
