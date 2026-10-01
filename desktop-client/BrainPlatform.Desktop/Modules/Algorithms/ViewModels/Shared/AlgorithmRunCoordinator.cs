using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using System.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

internal sealed class AlgorithmRunCoordinator(IAlgorithmClient client)
{
    // Dynamic official algorithms operate on the complete recording. A real
    // recording can legitimately take longer than the old fixed 30 s limit,
    // so do not return a still-running Run as if it were a finished result.
    private static readonly TimeSpan RunWaitTimeout = TimeSpan.FromMinutes(5);
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

        var stopwatch = Stopwatch.StartNew();
        while (run.Status is "queued" or "running")
        {
            if (stopwatch.Elapsed >= RunWaitTimeout)
            {
                throw new TimeoutException($"算法运行超过 {RunWaitTimeout.TotalMinutes:0} 分钟仍未完成（Run：{run.RunId}）。");
            }

            await Task.Delay(PollInterval, cancellationToken);
            run = await client.GetRunAsync(run.RunId, cancellationToken);
            onStatusChanged(run);
        }

        return run;
    }
}
