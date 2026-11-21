using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

/// <summary>
/// Sends only EEG V samples across the local HTTP process boundary. The
/// backend owns retention and range/gap semantics; this class owns no buffer.
/// </summary>
public sealed class HttpLiveSampleAccessBridge : ILiveSampleAccessBridge
{
    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim operations = new(1, 1);
    private Guid? activeSession;
    private int activeSamplingRateHz;
    private bool disposed;
    private readonly Dictionary<string, AlgorithmState> algorithms = new(StringComparer.OrdinalIgnoreCase);
    private int enabledCount;

    private sealed class AlgorithmState(JsonElement config)
    {
        internal JsonElement Config { get; } = config.Clone();
        internal bool Started { get; set; }
        internal long Revision { get; set; } = -1;
        internal CancellationTokenSource? FirstResultPoll { get; set; }

        internal void CancelFirstResultPoll()
        {
            FirstResultPoll?.Cancel();
            FirstResultPoll?.Dispose();
            FirstResultPoll = null;
        }
    }

    public bool HasEnabledAlgorithms => Volatile.Read(ref enabledCount) > 0;

    public event EventHandler<LiveAnalysisUpdate>? AnalysisUpdated;

    public HttpLiveSampleAccessBridge(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task PublishAsync(AcquisitionAnalysisBatch analysis, CancellationToken cancellationToken)
    {
        await operations.WaitAsync(cancellationToken);
        try { await PublishCoreAsync(analysis, cancellationToken); }
        finally { operations.Release(); }
    }

    private async Task PublishCoreAsync(AcquisitionAnalysisBatch analysis, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var session = await EnsureSessionAsync(analysis, cancellationToken);
        var eegIndexes = analysis.Stream.Channels
            .Where(channel => channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
            .Select(channel => channel.StreamIndex)
            .ToArray();
        if (eegIndexes.Length == 0) throw new InvalidOperationException("实时访问至少需要一个 EEG 通道。");

        var values = new double[checked(analysis.Batch.SampleCount * eegIndexes.Length)];
        for (var sample = 0; sample < analysis.Batch.SampleCount; sample++)
        {
            for (var channel = 0; channel < eegIndexes.Length; channel++)
            {
                values[sample * eegIndexes.Length + channel] = analysis.Batch.SampleMajorValues[
                    sample * analysis.Batch.ChannelCount + eegIndexes[channel]];
            }
        }

        using var content = new ByteArrayContent(MemoryMarshal.AsBytes(values.AsSpan()).ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.brain-platform.float64");
        foreach (var (algorithmId, algorithm) in algorithms)
        {
            if (!algorithm.Started) await StartAlgorithmAsync(session, algorithmId, algorithm, analysis.Stream.SamplingRateHz, cancellationToken);
        }

        using var response = await httpClient.PostAsync(
            $"api/live-recordings/sessions/{session:N}/batches/binary?first_sample_counter={analysis.Batch.FirstSampleCounter}&sample_count={analysis.Batch.SampleCount}",
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var acknowledgement = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
        if (acknowledgement is null) throw new InvalidOperationException("LIVE_SAMPLE_ACK_INVALID: 缺少实时样本应答。");
        var root = acknowledgement.RootElement;
        if (root.GetProperty("type").GetString() != "live_samples_accepted" ||
            root.GetProperty("schema_version").GetString() != "live-sample-ack-v2" ||
            !Guid.TryParse(root.GetProperty("session_id").GetString(), out var returnedSession) || returnedSession != session)
            throw new InvalidOperationException("LIVE_SAMPLE_ACK_INVALID: 实时样本应答版本或会话身份不匹配。");
        foreach (var value in root.GetProperty("updates").EnumerateArray())
        {
            var update = LiveAnalysisUpdate.Read(value, session, analysis.Stream.SamplingRateHz);
            if (algorithms.TryGetValue(update.AlgorithmId, out var algorithm)) PublishUpdate(algorithm, update);
        }
    }

    public async Task SetAlgorithmEnabledAsync(string algorithmId, bool enabled, JsonElement config, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithmId);
        if (enabled && config.ValueKind != JsonValueKind.Object) throw new ArgumentException("实时算法配置必须为对象。", nameof(config));
        await operations.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (activeSession is { } session && algorithms.TryGetValue(algorithmId, out var previous) && previous.Started)
            {
                using var response = await httpClient.DeleteAsync(AlgorithmPath(session, algorithmId), cancellationToken);
                response.EnsureSuccessStatusCode();
            }
            if (algorithms.Remove(algorithmId, out var retired)) retired.CancelFirstResultPoll();
            if (enabled) algorithms[algorithmId] = new AlgorithmState(config);
            Volatile.Write(ref enabledCount, algorithms.Count);
            if (enabled && activeSession is { } active)
            {
                try
                {
                    await StartAlgorithmAsync(active, algorithmId, algorithms[algorithmId], activeSamplingRateHz, cancellationToken);
                }
                catch
                {
                    if (algorithms.Remove(algorithmId, out var failed)) failed.CancelFirstResultPoll();
                    Volatile.Write(ref enabledCount, algorithms.Count);
                    throw;
                }
            }
        }
        finally { operations.Release(); }
    }

    private async Task StartAlgorithmAsync(Guid session, string algorithmId, AlgorithmState algorithm,
        int samplingRateHz, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(AlgorithmPath(session, algorithmId),
            new { schema_version = "live-analysis-config-v1", config = algorithm.Config }, cancellationToken);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
        algorithm.Started = true;
        if (response.IsSuccessStatusCode && document is not null)
        {
            var update = LiveAnalysisUpdate.Read(document.RootElement, session, samplingRateHz);
            if (!string.Equals(update.AlgorithmId, algorithmId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("LIVE_ANALYSIS_UPDATE_INVALID: 启动应答算法身份不匹配。");
            PublishUpdate(algorithm, update);
            if (update.State == "warming_up")
            {
                var poll = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                algorithm.FirstResultPoll = poll;
                _ = PollFirstResultAsync(session, algorithmId, algorithm, samplingRateHz, poll.Token);
            }
        }
        else
        {
            var failure = document?.RootElement;
            AnalysisUpdated?.Invoke(this, new LiveAnalysisUpdate(session, algorithmId, 0, "unavailable", samplingRateHz,
                FailureCode: failure?.GetProperty("code").GetString() ?? "LIVE_ANALYSIS_START_FAILED",
                Message: failure?.GetProperty("message").GetString() ?? "实时算法启动失败。"));
        }
    }

    private async Task PollFirstResultAsync(Guid session, string algorithmId, AlgorithmState algorithm,
        int samplingRateHz, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                using var document = await httpClient.GetFromJsonAsync<JsonDocument>(
                    AlgorithmPath(session, algorithmId), cancellationToken);
                if (document is null) throw new InvalidOperationException("实时算法结果为空。");
                var update = LiveAnalysisUpdate.Read(document.RootElement, session, samplingRateHz);
                if (!string.Equals(update.AlgorithmId, algorithmId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("实时算法结果身份不匹配。");
                await operations.WaitAsync(cancellationToken);
                try
                {
                    if (activeSession != session || !algorithms.TryGetValue(algorithmId, out var current) ||
                        !ReferenceEquals(current, algorithm)) return;
                    PublishUpdate(algorithm, update);
                    if (algorithm.Revision > 0) return;
                }
                finally { operations.Release(); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            DesktopLog.Warning(
                $"live_analysis_first_result_poll_failed session_id={session:N} algorithm_id={algorithmId} detail={exception.Message}");
        }
    }

    private void PublishUpdate(AlgorithmState algorithm, LiveAnalysisUpdate update)
    {
        if (update.Revision <= algorithm.Revision) return;
        algorithm.Revision = update.Revision;
        if (update.Revision > 0) algorithm.CancelFirstResultPoll();
        AnalysisUpdated?.Invoke(this, update);
    }

    private static string AlgorithmPath(Guid session, string algorithmId) =>
        $"api/live-recordings/sessions/{session:N}/algorithms/{Uri.EscapeDataString(algorithmId)}";

    public async Task CloseSessionAsync(CancellationToken cancellationToken)
    {
        await operations.WaitAsync(cancellationToken);
        try { await CloseSessionCoreAsync(cancellationToken); }
        finally { operations.Release(); }
    }

    private async Task CloseSessionCoreAsync(CancellationToken cancellationToken)
    {
        var session = ClearSession();
        if (session is { } id)
        {
            using var response = await httpClient.DeleteAsync($"api/live-recordings/sessions/{id:N}", cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await operations.WaitAsync();
        try { await DisposeCoreAsync(); }
        finally { operations.Release(); }
    }

    private async ValueTask DisposeCoreAsync()
    {
        if (disposed) return;
        disposed = true;
        var session = ClearSession();
        if (session is { } id)
        {
            try { using var _ = await httpClient.DeleteAsync($"api/live-recordings/sessions/{id:N}"); }
            catch (HttpRequestException) { }
        }
    }

    private Guid? ClearSession()
    {
        var session = activeSession;
        activeSession = null;
        foreach (var algorithm in algorithms.Values) algorithm.CancelFirstResultPoll();
        algorithms.Clear();
        Volatile.Write(ref enabledCount, 0);
        return session;
    }

    private async Task<Guid> EnsureSessionAsync(AcquisitionAnalysisBatch analysis, CancellationToken cancellationToken)
    {
        if (activeSession is { } existing && existing == analysis.SessionId) return existing;
        if (activeSession is not null) throw new InvalidOperationException("实时访问会话不能跨采集会话复用。");

        var eegChannels = analysis.Stream.Channels
            .Where(channel => channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
            .ToArray();
        if (eegChannels.Any(channel => !string.Equals(channel.Unit, "V", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("实时访问边界要求 EEG 通道单位为 V。");
        }
        var request = new LiveAccessSessionRequest(
            analysis.SessionId.ToString("N"),
            $"{analysis.Stream.DeviceId}:{analysis.Stream.SamplingRateHz}",
            "acquisition-eeg-v1",
            analysis.Stream.SamplingRateHz,
            eegChannels.Select(channel => channel.Label ?? $"channel-{channel.StreamIndex}").ToArray(),
            eegChannels.Select(channel => channel.Unit).ToArray(),
            Math.Max(30_000, checked(12 * analysis.Stream.SamplingRateHz)));
        using var response = await httpClient.PostAsJsonAsync("api/live-recordings/sessions", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        activeSamplingRateHz = analysis.Stream.SamplingRateHz;
        activeSession = analysis.SessionId;
        return analysis.SessionId;
    }

    private sealed record LiveAccessSessionRequest(
        [property: JsonPropertyName("session_id")] string SessionId,
        [property: JsonPropertyName("stream_revision")] string StreamRevision,
        [property: JsonPropertyName("schema_revision")] string SchemaRevision,
        [property: JsonPropertyName("sampling_rate_hz")] int SamplingRateHz,
        [property: JsonPropertyName("channel_names")] string[] ChannelNames,
        [property: JsonPropertyName("channel_units")] string[] ChannelUnits,
        [property: JsonPropertyName("capacity_samples")] int CapacitySamples);
}
