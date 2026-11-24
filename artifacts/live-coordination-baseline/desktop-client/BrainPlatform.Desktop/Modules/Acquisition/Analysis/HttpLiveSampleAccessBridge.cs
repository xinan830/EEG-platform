using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

/// <summary>
/// Sends only EEG V samples across the local HTTP process boundary. The
/// backend owns retention and range/gap semantics; this class owns no buffer.
/// </summary>
public sealed class HttpLiveSampleAccessBridge : ILiveSampleAccessBridge
{
    private readonly HttpClient httpClient;
    private readonly object gate = new();
    private Guid? activeSession;
    private bool disposed;

    public HttpLiveSampleAccessBridge(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task PublishAsync(AcquisitionAnalysisBatch analysis, CancellationToken cancellationToken)
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
        using var response = await httpClient.PostAsync(
            $"api/live-recordings/sessions/{session:N}/batches/binary?first_sample_counter={analysis.Batch.FirstSampleCounter}&sample_count={analysis.Batch.SampleCount}",
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        Guid? session;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            session = activeSession;
            activeSession = null;
        }
        if (session is { } id)
        {
            try { using var _ = await httpClient.DeleteAsync($"api/live-recordings/sessions/{id:N}"); }
            catch (HttpRequestException) { }
        }
    }

    private async Task<Guid> EnsureSessionAsync(AcquisitionAnalysisBatch analysis, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (activeSession is { } existing && existing == analysis.SessionId) return existing;
            if (activeSession is not null) throw new InvalidOperationException("实时访问会话不能跨采集会话复用。");
        }

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
            30_000);
        using var response = await httpClient.PostAsJsonAsync("api/live-recordings/sessions", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        lock (gate) activeSession = analysis.SessionId;
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
