using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>PSD's review adapter. Scientific values remain backend-owned.</summary>
public sealed class PsdReviewAnalysisProvider(IAlgorithmClient client) : IReviewAnalysisProvider
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    public string AlgorithmId => "psd";

    public async Task<AlgorithmCatalogItem> GetAlgorithmAsync(CancellationToken cancellationToken) =>
        (await client.ListAlgorithmsAsync(cancellationToken)).Single(item => item.Id == AlgorithmId && item.IsRunnable);

    public Task<RegisteredRecording> RegisterAsync(string directory, CancellationToken cancellationToken) =>
        (client as IRecordingRegistrationClient ?? throw new InvalidOperationException("算法客户端不支持记录注册。"))
        .RegisterWpfRecordingAsync(directory, cancellationToken);

    public async Task<AnalysisRunResponse> EnsureRunAsync(
        AnalysisRunRequest request,
        Action<AnalysisRunResponse>? onStatusChanged,
        CancellationToken cancellationToken)
    {
        var run = await client.EnsureRunAsync(request, cancellationToken);
        onStatusChanged?.Invoke(run);
        while (run.Status is "queued" or "running")
        {
            await Task.Delay(PollInterval, cancellationToken);
            run = await client.GetRunAsync(run.RunId, cancellationToken);
            onStatusChanged?.Invoke(run);
        }
        return run;
    }

    public async Task<WindowPreviewResponse> LoadAsync(
        string recordingId,
        string runId,
        long cursorSample,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recordingId))
            throw new ArgumentException("记录 ID 不能为空。", nameof(recordingId));
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("Run ID 不能为空。", nameof(runId));
        if (cursorSample < 0)
            throw new ArgumentOutOfRangeException(nameof(cursorSample));

        var result = await client.GetWindowPreviewAsync(runId, cursorSample, null, cancellationToken);
        if (!string.Equals(result.RecordingId, recordingId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("算法结果不属于当前回溯记录，已拒绝绑定。");
        }

        return result;
    }
}
