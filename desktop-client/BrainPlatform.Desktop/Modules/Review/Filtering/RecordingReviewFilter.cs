using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Review.Filtering;

public sealed record RecordingReviewFilterSettings(double HighPassHz, double LowPassHz, double? NotchHz)
{
    public void Validate(int samplingRateHz)
    {
        if (!double.IsFinite(HighPassHz) || !double.IsFinite(LowPassHz) ||
            HighPassHz <= 0 || LowPassHz <= HighPassHz)
        {
            throw new ArgumentException("高通必须大于 0，且必须小于低通。");
        }

        var nyquist = samplingRateHz / 2d;
        if (LowPassHz >= nyquist)
        {
            throw new ArgumentException($"低通必须小于奈奎斯特频率 {nyquist:g} Hz。");
        }

        if (NotchHz is not null && (NotchHz is not (50d or 60d) || NotchHz >= nyquist))
        {
            throw new ArgumentException("陷波仅支持关闭、50 Hz 或 60 Hz，并且必须小于奈奎斯特频率。");
        }
    }
}

public interface IRecordingReviewFilter
{
    Task<ReviewFilterContract> GetContractAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ReviewFilterContract.Unknown);

    Task<RecordingReviewWindow> ApplyAsync(
        RecordingReviewWindow source,
        LocalRawRecordingManifest manifest,
        RecordingReviewFilterSettings settings,
        CancellationToken cancellationToken);

    Task<RecordingReviewWindow> ApplyWithWarmupAsync(
        RecordingReviewWindow? warmup,
        RecordingReviewWindow source,
        LocalRawRecordingManifest manifest,
        RecordingReviewFilterSettings settings,
        CancellationToken cancellationToken) => ApplyAsync(source, manifest, settings, cancellationToken);

    async Task<RecordingReviewFilterChunkResult> ApplyChunkWithCheckpointAsync(
        string? checkpoint,
        RecordingReviewWindow source,
        LocalRawRecordingManifest manifest,
        RecordingReviewFilterSettings settings,
        CancellationToken cancellationToken)
    {
        var window = await ApplyAsync(source, manifest, settings, cancellationToken);
        return new RecordingReviewFilterChunkResult(window, null);
    }

    Task<IRecordingReviewCheckpointSession?> OpenCheckpointSessionAsync(
        LocalRawRecordingManifest manifest,
        RecordingReviewFilterSettings settings,
        CancellationToken cancellationToken) =>
        Task.FromResult<IRecordingReviewCheckpointSession?>(null);
}

public interface IRecordingReviewCheckpointSession : IAsyncDisposable
{
    Task RestoreAsync(string checkpoint, CancellationToken cancellationToken);
    Task AdvanceAsync(RecordingReviewSegment source, CancellationToken cancellationToken);
    Task<RecordingReviewSegment> FilterAsync(RecordingReviewSegment source, CancellationToken cancellationToken);
    Task<string> ExportAsync(CancellationToken cancellationToken);
}

public sealed record RecordingReviewFilterChunkResult(
    RecordingReviewWindow Window,
    string? Checkpoint);

/// <summary>Transient Python-owned display filtering. It never writes the raw recording.</summary>
public sealed class HttpRecordingReviewFilter(HttpClient httpClient) : IRecordingReviewFilter
{
    public async Task<ReviewFilterContract> GetContractAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("api/live-filters/contract", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var contract = document.RootElement.GetProperty("filter_contract");
        var algorithmVersion = contract.GetProperty("algorithm_version").GetString();
        if (string.IsNullOrWhiteSpace(algorithmVersion))
        {
            throw new InvalidOperationException("滤波服务没有返回有效的算法契约版本。");
        }

        var checkpointVersion = contract.TryGetProperty("checkpoint_version", out var version)
            ? version.GetString() ?? "unknown"
            : "unknown";
        return new ReviewFilterContract(algorithmVersion,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contract.GetRawText()))).ToLowerInvariant(),
            checkpointVersion);
    }

    public async Task<IRecordingReviewCheckpointSession?> OpenCheckpointSessionAsync(
        LocalRawRecordingManifest manifest,
        RecordingReviewFilterSettings settings,
        CancellationToken cancellationToken)
    {
        settings.Validate(manifest.SamplingRateHz);
        var eegIndexes = manifest.Channels
            .Where(channel => channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
            .Select(channel => channel.StreamIndex)
            .ToArray();
        var sessionId = $"review-{manifest.SessionId:N}-{Guid.NewGuid():N}";
        using var response = await httpClient.PostAsJsonAsync(
            "api/live-filters/sessions",
            new FilterSessionRequest(sessionId, manifest.SamplingRateHz, manifest.Channels.Count, eegIndexes,
                settings.HighPassHz, settings.LowPassHz, settings.NotchHz),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return new HttpCheckpointSession(httpClient, sessionId);
    }

    public async Task<RecordingReviewWindow> ApplyAsync(
        RecordingReviewWindow source,
        LocalRawRecordingManifest manifest,
        RecordingReviewFilterSettings settings,
        CancellationToken cancellationToken)
        => await ApplyWithWarmupAsync(null, source, manifest, settings, cancellationToken);

    public async Task<RecordingReviewWindow> ApplyWithWarmupAsync(
        RecordingReviewWindow? warmup,
        RecordingReviewWindow source,
        LocalRawRecordingManifest manifest,
        RecordingReviewFilterSettings settings,
        CancellationToken cancellationToken)
    {
        settings.Validate(manifest.SamplingRateHz);
        var eegIndexes = manifest.Channels
            .Where(channel => channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
            .Select(channel => channel.StreamIndex)
            .ToArray();
        var filteredSegments = new List<RecordingReviewSegment>(source.Segments.Count);
        foreach (var segment in source.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sessionId = $"review-{manifest.SessionId:N}-{Guid.NewGuid():N}";
            try
            {
                using var createResponse = await httpClient.PostAsJsonAsync(
                    "api/live-filters/sessions",
                    new FilterSessionRequest(
                        sessionId,
                        manifest.SamplingRateHz,
                        segment.ChannelCount,
                        eegIndexes,
                        settings.HighPassHz,
                        settings.LowPassHz,
                        settings.NotchHz),
                    cancellationToken);
                createResponse.EnsureSuccessStatusCode();

                var warmupSegment = FindContiguousWarmupSegment(warmup, segment);
                if (warmupSegment is not null)
                {
                    await WarmupAsync(sessionId, warmupSegment, cancellationToken);
                }

                var bytes = await FilterSegmentAsync(sessionId, segment, cancellationToken);
                var payloadLength = checked(segment.SampleMajorValues.Length * sizeof(double));
                if (bytes.Length != payloadLength)
                {
                    throw new InvalidOperationException("回溯滤波返回的数据长度与原始窗口不一致。");
                }

                var values = new double[segment.SampleMajorValues.Length];
                Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
                filteredSegments.Add(segment with { SampleMajorValues = values });
            }
            finally
            {
                try
                {
                    using var _ = await httpClient.DeleteAsync(
                        $"api/live-filters/sessions/{sessionId}",
                        CancellationToken.None);
                }
                catch (HttpRequestException)
                {
                    // The transient session has no persistence or raw-data ownership.
                }
            }
        }

        return source with { Segments = filteredSegments };
    }

    public async Task<RecordingReviewFilterChunkResult> ApplyChunkWithCheckpointAsync(
        string? checkpoint,
        RecordingReviewWindow source,
        LocalRawRecordingManifest manifest,
        RecordingReviewFilterSettings settings,
        CancellationToken cancellationToken)
    {
        settings.Validate(manifest.SamplingRateHz);
        if (source.Segments.Count != 1)
        {
            throw new ArgumentException("带 checkpoint 的回溯滤波块必须是单个连续片段。", nameof(source));
        }

        var segment = source.Segments[0];
        var eegIndexes = manifest.Channels
            .Where(channel => channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
            .Select(channel => channel.StreamIndex)
            .ToArray();
        var sessionId = $"review-{manifest.SessionId:N}-{Guid.NewGuid():N}";
        try
        {
            using var createResponse = await httpClient.PostAsJsonAsync(
                "api/live-filters/sessions",
                new FilterSessionRequest(
                    sessionId,
                    manifest.SamplingRateHz,
                    segment.ChannelCount,
                    eegIndexes,
                    settings.HighPassHz,
                    settings.LowPassHz,
                    settings.NotchHz),
                cancellationToken);
            createResponse.EnsureSuccessStatusCode();

            if (checkpoint is not null)
            {
                using var checkpointResponse = await httpClient.PutAsJsonAsync(
                    $"api/live-filters/sessions/{sessionId}/checkpoint",
                    new FilterCheckpointRequest(checkpoint),
                    cancellationToken);
                checkpointResponse.EnsureSuccessStatusCode();
            }

            var bytes = await FilterSegmentAsync(sessionId, segment, cancellationToken);
            var payloadLength = checked(segment.SampleMajorValues.Length * sizeof(double));
            if (bytes.Length != payloadLength)
            {
                throw new InvalidOperationException("回溯滤波返回的数据长度与原始窗口不一致。");
            }

            var values = new double[segment.SampleMajorValues.Length];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            using var checkpointRead = await httpClient.GetAsync(
                $"api/live-filters/sessions/{sessionId}/checkpoint",
                cancellationToken);
            checkpointRead.EnsureSuccessStatusCode();
            var checkpointPayload = await checkpointRead.Content.ReadFromJsonAsync<FilterCheckpointResponse>(cancellationToken)
                ?? throw new InvalidOperationException("回溯滤波没有返回 checkpoint。");
            return new RecordingReviewFilterChunkResult(
                source with { Segments = [segment with { SampleMajorValues = values }] },
                checkpointPayload.CheckpointB64);
        }
        finally
        {
            try
            {
                using var _ = await httpClient.DeleteAsync(
                    $"api/live-filters/sessions/{sessionId}",
                    CancellationToken.None);
            }
            catch (HttpRequestException)
            {
                // The transient session has no persistence or raw-data ownership.
            }
        }
    }

    private async Task WarmupAsync(string sessionId, RecordingReviewSegment segment, CancellationToken cancellationToken)
    {
        using var content = CreateBinaryContent(segment.SampleMajorValues);
        using var response = await httpClient.PostAsync(
            $"api/live-filters/sessions/{sessionId}/warmup/binary?sample_count={segment.SampleCount}",
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<byte[]> FilterSegmentAsync(string sessionId, RecordingReviewSegment segment, CancellationToken cancellationToken)
    {
        using var content = CreateBinaryContent(segment.SampleMajorValues);
        using var response = await httpClient.PostAsync(
            $"api/live-filters/sessions/{sessionId}/batches/binary?sample_count={segment.SampleCount}",
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static ByteArrayContent CreateBinaryContent(double[] values)
    {
        var content = new ByteArrayContent(MemoryMarshal.AsBytes(values.AsSpan()).ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.brain-platform.float64");
        return content;
    }

    private static RecordingReviewSegment? FindContiguousWarmupSegment(
        RecordingReviewWindow? warmup,
        RecordingReviewSegment source)
    {
        return warmup?.Segments.LastOrDefault(segment =>
            segment.ChannelCount == source.ChannelCount &&
            segment.LastSampleCounter + 1L == source.FirstSampleCounter);
    }

    private sealed record FilterSessionRequest(
        [property: JsonPropertyName("session_id")] string SessionId,
        [property: JsonPropertyName("sampling_rate_hz")] int SamplingRateHz,
        [property: JsonPropertyName("channel_count")] int ChannelCount,
        [property: JsonPropertyName("eeg_channel_indexes")] int[] EegChannelIndexes,
        [property: JsonPropertyName("low_cut_hz")] double LowCutHz,
        [property: JsonPropertyName("high_cut_hz")] double HighCutHz,
        [property: JsonPropertyName("notch_hz")] double? NotchHz);

    private sealed record FilterCheckpointRequest(
        [property: JsonPropertyName("checkpoint_b64")] string CheckpointB64);

    private sealed record FilterCheckpointResponse(
        [property: JsonPropertyName("checkpoint_b64")] string CheckpointB64,
        [property: JsonPropertyName("version")] string Version);

    private sealed class HttpCheckpointSession(HttpClient client, string sessionId) : IRecordingReviewCheckpointSession
    {
        private int disposed;

        public async Task RestoreAsync(string checkpoint, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            using var response = await client.PutAsJsonAsync(
                $"api/live-filters/sessions/{sessionId}/checkpoint",
                new FilterCheckpointRequest(checkpoint), cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        public async Task AdvanceAsync(RecordingReviewSegment source, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            using var content = CreateBinaryContent(source.SampleMajorValues);
            using var response = await client.PostAsync(
                $"api/live-filters/sessions/{sessionId}/warmup/binary?sample_count={source.SampleCount}",
                content, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        public async Task<RecordingReviewSegment> FilterAsync(
            RecordingReviewSegment source,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            using var content = CreateBinaryContent(source.SampleMajorValues);
            using var response = await client.PostAsync(
                $"api/live-filters/sessions/{sessionId}/batches/binary?sample_count={source.SampleCount}",
                content, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var expected = checked(source.SampleMajorValues.Length * sizeof(double));
            if (bytes.Length != expected)
                throw new InvalidOperationException("回溯滤波返回的数据长度与原始窗口不一致。");
            var values = new double[source.SampleMajorValues.Length];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return source with { SampleMajorValues = values };
        }

        public async Task<string> ExportAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            using var response = await client.GetAsync(
                $"api/live-filters/sessions/{sessionId}/checkpoint", cancellationToken);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<FilterCheckpointResponse>(cancellationToken)
                ?? throw new InvalidOperationException("回溯滤波没有返回 checkpoint。");
            return payload.CheckpointB64;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try
            {
                using var _ = await client.DeleteAsync(
                    $"api/live-filters/sessions/{sessionId}", CancellationToken.None);
            }
            catch (HttpRequestException)
            {
                // A transient review session owns no persistent scientific data.
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
                throw new ObjectDisposedException(nameof(HttpCheckpointSession));
        }
    }
}
