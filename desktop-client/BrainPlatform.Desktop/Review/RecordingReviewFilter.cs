using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Review;

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
}

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

        return new ReviewFilterContract(algorithmVersion,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contract.GetRawText()))).ToLowerInvariant());
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
}
