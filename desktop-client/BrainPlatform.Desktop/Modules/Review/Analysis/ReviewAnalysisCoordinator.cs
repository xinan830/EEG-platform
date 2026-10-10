using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Latest-only coordinator for one review analysis provider.</summary>
public sealed class ReviewAnalysisCoordinator
{
    private readonly Func<string, long?, double?, CancellationToken, Task<WindowPreviewResponse>>? loadWindow;
    private readonly IReviewAnalysisProvider? provider;
    private long revision;

    public ReviewAnalysisCoordinator(Func<string, long?, double?, CancellationToken, Task<WindowPreviewResponse>> loadWindow)
        => this.loadWindow = loadWindow ?? throw new ArgumentNullException(nameof(loadWindow));

    public ReviewAnalysisCoordinator(IReviewAnalysisProvider provider)
        => this.provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public ReviewAnalysisContext? Context { get; private set; }
    public WindowPreviewResponse? CurrentWindow { get; private set; }

    public Task<AlgorithmCatalogItem> GetAlgorithmAsync(CancellationToken cancellationToken) =>
        (provider ?? throw new InvalidOperationException("未配置分析 provider。")) .GetAlgorithmAsync(cancellationToken);

    public Task<RegisteredRecording> RegisterAsync(string directory, CancellationToken cancellationToken) =>
        (provider ?? throw new InvalidOperationException("未配置分析 provider。"))
        .RegisterAsync(directory, cancellationToken);

    public Task<AnalysisRunResponse> EnsureRunAsync(
        AnalysisRunRequest request,
        Action<AnalysisRunResponse>? onStatusChanged,
        CancellationToken cancellationToken)
    {
        if (provider is null)
            throw new InvalidOperationException("当前协调器未配置可执行 provider。");
        return provider.EnsureRunAsync(request, onStatusChanged, cancellationToken);
    }

    public async Task<WindowPreviewResponse?> LoadAtSampleAsync(string recordingId, string runId, long cursorSample, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recordingId)) throw new ArgumentException("记录 ID 不能为空。", nameof(recordingId));
        if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("Run ID 不能为空。", nameof(runId));
        if (cursorSample < 0) throw new ArgumentOutOfRangeException(nameof(cursorSample));
        var requestedRevision = Interlocked.Increment(ref revision);
        var result = provider is not null
            ? await provider.LoadAsync(recordingId, runId, cursorSample, cancellationToken)
            : await (loadWindow ?? throw new InvalidOperationException("未配置分析 provider。"))(runId, cursorSample, null, cancellationToken);
        if (requestedRevision != Volatile.Read(ref revision)) return null;
        Context = new ReviewAnalysisContext(recordingId, runId, result.CursorSample, result.CursorSeconds);
        CurrentWindow = result;
        return result;
    }

    public void Reset()
    {
        Interlocked.Increment(ref revision);
        Context = null;
        CurrentWindow = null;
    }
}
