using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels;

internal sealed class AlgorithmRunCoordinator(IAlgorithmClient client)
{
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

        for (var attempt = 0; attempt < 120 && (run.Status is "queued" or "running"); attempt++)
        {
            await Task.Delay(250, cancellationToken);
            run = await client.GetRunAsync(run.RunId, cancellationToken);
            onStatusChanged(run);
        }

        return run;
    }
}
