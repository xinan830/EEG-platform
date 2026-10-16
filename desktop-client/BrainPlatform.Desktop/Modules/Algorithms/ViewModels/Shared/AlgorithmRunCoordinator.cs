using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

internal sealed class AlgorithmRunCoordinator(IAlgorithmClient client)
{
    // The backend owns execution lifetime. Do not turn a still-running Run
    // into a false frontend failure because a fixed client timeout elapsed.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    internal async Task<RegisteredRecording> RegisterRecordingAsync(
        string sourceDirectory,
        CancellationToken cancellationToken)
    {
        var registration = client as IRecordingRegistrationClient
            ?? throw new InvalidOperationException("当前算法客户端不支持 WPF 记录注册。");
        return await registration.RegisterWpfRecordingAsync(sourceDirectory, cancellationToken);
    }

    internal async Task<AnalysisRunResponse> CreateRunAndWaitAsync(
        AnalysisRunRequest request,
        Action<AnalysisRunResponse> onStatusChanged,
        CancellationToken cancellationToken)
    {
        var run = await client.CreateRunAsync(request, cancellationToken);
        onStatusChanged(run);
        return await ObserveRunAndWaitAsync(run, onStatusChanged, cancellationToken);
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
