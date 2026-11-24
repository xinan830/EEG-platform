using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

internal sealed class AlgorithmRunCoordinator(IAlgorithmClient client)
{
    // The backend owns execution lifetime. Do not turn a still-running Run
    // into a false frontend failure because a fixed client timeout elapsed.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private readonly object foregroundGate = new();
    private CancellationTokenSource? foregroundLifetime;
    private string? foregroundRunId;
    private long foregroundGeneration;

    internal bool CanStartForeground(long generation)
    {
        lock (foregroundGate)
            return foregroundLifetime is null || foregroundGeneration < generation;
    }

    internal void SupersedeForeground(long generation)
    {
        CancellationTokenSource? previous;
        string? runId;
        lock (foregroundGate)
        {
            if (generation <= foregroundGeneration) return;
            foregroundGeneration = generation;
            previous = foregroundLifetime;
            runId = foregroundRunId;
            foregroundLifetime = null;
            foregroundRunId = null;
        }

        previous?.Cancel();
        previous?.Dispose();
        if (runId is not null) _ = CancelSupersededRunAsync(runId);
    }

    internal async Task<RegisteredRecording> RegisterRecordingAsync(
        string sourceDirectory,
        CancellationToken cancellationToken,
        IReadOnlyList<MontageProfile>? analysisMontageProfiles = null)
    {
        var registration = client as IRecordingRegistrationClient
            ?? throw new InvalidOperationException("当前算法客户端不支持 WPF 记录注册。");
        return await registration.RegisterWpfRecordingAsync(sourceDirectory, cancellationToken, analysisMontageProfiles);
    }

    internal async Task<AnalysisRunResponse> CreateRunAndWaitAsync(
        AnalysisRunRequest request,
        Action<AnalysisRunResponse> onStatusChanged,
        CancellationToken cancellationToken,
        long generation = 0,
        bool foreground = true)
    {
        if (!foreground)
        {
            var backgroundRun = await client.CreateRunAsync(request, cancellationToken);
            onStatusChanged(backgroundRun);
            return await ObserveRunAndWaitAsync(backgroundRun, onStatusChanged, cancellationToken);
        }
        var lifetime = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (foregroundGate)
        {
            previous = foregroundLifetime;
            foregroundLifetime = lifetime;
            foregroundRunId = null;
            foregroundGeneration = Math.Max(foregroundGeneration, generation);
        }
        previous?.Cancel();
        previous?.Dispose();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        try
        {
            // Let creation return its durable Run ID even after supersession;
            // cancelling this HTTP request could orphan a server-side Run.
            var run = await client.CreateRunAsync(request, cancellationToken);
            var isCurrent = false;
            lock (foregroundGate)
            {
                isCurrent = ReferenceEquals(foregroundLifetime, lifetime) && foregroundGeneration == generation;
                if (isCurrent) foregroundRunId = run.RunId;
            }
            if (!isCurrent)
            {
                _ = CancelSupersededRunAsync(run.RunId);
                throw new OperationCanceledException("Run 已被更新的分析配置取代。", linked.Token);
            }
            onStatusChanged(run);
            return await ObserveRunAndWaitAsync(run, onStatusChanged, linked.Token);
        }
        finally
        {
            lock (foregroundGate)
            {
                if (ReferenceEquals(foregroundLifetime, lifetime))
                {
                    foregroundLifetime = null;
                    foregroundRunId = null;
                }
            }
            lifetime.Dispose();
        }
    }

    private async Task CancelSupersededRunAsync(string runId)
    {
        try { await client.CancelRunAsync(runId, CancellationToken.None); }
        catch (AlgorithmApiException error) when (error.Code == "RUN_NOT_CANCELLABLE") { }
        catch { }
    }

    internal async Task<AnalysisRunResponse> EnsureRunAndWaitAsync(
        AnalysisRunRequest request,
        Action<AnalysisRunResponse> onStatusChanged,
        CancellationToken cancellationToken)
    {
        var run = await client.EnsureRunAsync(request, cancellationToken);
        onStatusChanged(run);
        return await ObserveRunAndWaitAsync(run, onStatusChanged, cancellationToken);
    }

    internal async Task<AnalysisRunResponse> ObserveRunAndWaitAsync(
        AnalysisRunResponse run,
        Action<AnalysisRunResponse> onStatusChanged,
        CancellationToken cancellationToken)
    {
        while (run.Status is "queued" or "running")
        {
            await Task.Delay(PollInterval, cancellationToken);
            run = await client.GetRunAsync(run.RunId, cancellationToken);
            onStatusChanged(run);
        }

        return run;
    }
}
