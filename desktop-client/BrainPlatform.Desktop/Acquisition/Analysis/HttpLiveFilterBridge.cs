using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Acquisition.Analysis;

public sealed record LiveDisplayFilterSettings(double LowCutHz, double HighCutHz, double? NotchHz)
{
    public void Validate()
    {
        if (!(LowCutHz > 0 && HighCutHz > LowCutHz))
        {
            throw new ArgumentException("高通必须大于 0，且必须小于低通。");
        }

        if (NotchHz is not null && NotchHz is not (50d or 60d))
        {
            throw new ArgumentException("陷波仅支持关闭、50 Hz 或 60 Hz。");
        }
    }
}

/// <summary>Local HTTP boundary for Python-owned causal display filtering.</summary>
public sealed class HttpLiveFilterBridge : IAcquisitionAnalysisBridge
{
    private readonly HttpClient httpClient;
    private readonly object gate = new();
    private LiveDisplayFilterSettings activeSettings = new(1, 30, null);
    private string? activeFilterSessionId;
    private long activeConfigurationRevision;
    private long nextConfigurationRevision;
    private ScheduledFilterChange? scheduledChange;
    private bool isUnavailable;

    public HttpLiveFilterBridge(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public bool IsUnavailable
    {
        get
        {
            lock (gate)
            {
                return isUnavailable;
            }
        }
    }

    public event EventHandler<FilteredDisplayBatch>? FilteredBatchAvailable;

    /// <summary>Sets the configuration that the next recording session starts with.</summary>
    public long Configure(LiveDisplayFilterSettings value)
    {
        value.Validate();
        lock (gate)
        {
            if (activeFilterSessionId is not null)
            {
                throw new InvalidOperationException("采集中请使用带时间边界的滤波切换。");
            }

            activeSettings = value;
            activeConfigurationRevision = ++nextConfigurationRevision;
            scheduledChange = null;
            isUnavailable = false;
            return activeConfigurationRevision;
        }
    }

    /// <summary>
    /// Schedules a display-only transition at the first sample after the raw
    /// counter observed when the user changed a control.
    /// </summary>
    public LiveDisplayFilterSchedule ScheduleChange(
        LiveDisplayFilterSettings value,
        long effectiveFromRawSampleCounter,
        LiveDisplayFilterWarmup? warmup)
    {
        value.Validate();
        if (effectiveFromRawSampleCounter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveFromRawSampleCounter));
        }

        lock (gate)
        {
            var revision = ++nextConfigurationRevision;
            var supersededPendingBoundary = scheduledChange?.EffectiveFromRawSampleCounter;
            scheduledChange = new ScheduledFilterChange(value, revision, effectiveFromRawSampleCounter, warmup);
            isUnavailable = false;
            return new LiveDisplayFilterSchedule(
                revision,
                effectiveFromRawSampleCounter,
                supersededPendingBoundary);
        }
    }

    public async Task PublishAsync(AcquisitionAnalysisBatch analysis, CancellationToken cancellationToken)
    {
        if (IsUnavailable)
        {
            return;
        }

        try
        {
            var remaining = analysis.Batch;
            while (true)
            {
                var change = GetScheduledChange();
                if (change is null || remaining.LastSampleCounter < change.EffectiveFromRawSampleCounter)
                {
                    await PublishActiveBatchAsync(analysis, remaining, cancellationToken);
                    return;
                }

                if (remaining.FirstSampleCounter < change.EffectiveFromRawSampleCounter)
                {
                    var oldSampleCount = checked((int)(change.EffectiveFromRawSampleCounter - remaining.FirstSampleCounter));
                    await PublishActiveBatchAsync(
                        analysis,
                        LiveFilterBatchSlicer.Slice(remaining, 0, oldSampleCount),
                        cancellationToken);
                    remaining = LiveFilterBatchSlicer.Slice(
                        remaining,
                        oldSampleCount,
                        remaining.SampleCount - oldSampleCount);
                }

                if (!await ActivateScheduledChangeAsync(
                        analysis,
                        change,
                        remaining.FirstSampleCounter,
                        cancellationToken))
                {
                    // A newer setting has a later effective counter. Re-evaluate
                    // this same raw remainder against that newer boundary.
                    continue;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            lock (gate)
            {
                isUnavailable = true;
                activeFilterSessionId = null;
            }

            throw;
        }
    }

    public async Task CloseAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        string? filterSessionId;
        lock (gate)
        {
            filterSessionId = activeFilterSessionId;
            activeFilterSessionId = null;
            scheduledChange = null;
            isUnavailable = false;
        }
        if (filterSessionId is null)
        {
            return;
        }

        try
        {
            using var _ = await httpClient.DeleteAsync($"api/live-filters/sessions/{filterSessionId}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Live filtering is transient; stopping raw acquisition must still succeed.
        }
    }

    private ScheduledFilterChange? GetScheduledChange()
    {
        lock (gate)
        {
            return scheduledChange;
        }
    }

    private async Task PublishActiveBatchAsync(
        AcquisitionAnalysisBatch analysis,
        AcquisitionBatch batch,
        CancellationToken cancellationToken)
    {
        var session = await EnsureActiveSessionAsync(analysis, cancellationToken);
        var requestPayload = MemoryMarshal.AsBytes(batch.SampleMajorValues.AsSpan()).ToArray();
        using var content = new ByteArrayContent(requestPayload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.brain-platform.float64");
        using var response = await httpClient.PostAsync(
            $"api/live-filters/sessions/{session.FilterSessionId}/batches/binary?sample_count={batch.SampleCount}",
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var responsePayload = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.Headers.TryGetValues("X-Sample-Count", out var sampleCounts) ||
            !int.TryParse(sampleCounts.SingleOrDefault(), out var returnedSampleCount) ||
            returnedSampleCount != batch.SampleCount ||
            !response.Headers.TryGetValues("X-Unit", out var units) ||
            !string.Equals(units.SingleOrDefault(), "V", StringComparison.Ordinal) ||
            responsePayload.Length != checked(batch.SampleMajorValues.Length * sizeof(double)))
        {
            throw new InvalidOperationException("实时滤波后端返回的数据契约无效。");
        }

        var filteredValues = new double[batch.SampleMajorValues.Length];
        Buffer.BlockCopy(responsePayload, 0, filteredValues, 0, responsePayload.Length);

        FilteredBatchAvailable?.Invoke(this, new FilteredDisplayBatch(
            analysis.SessionId,
            session.ConfigurationRevision,
            new AcquisitionBatch(
                batch.FirstSampleCounter,
                batch.SampleCount,
                batch.ChannelCount,
                filteredValues,
                batch.ReceivedAtUtc)));
    }

    private async Task<bool> ActivateScheduledChangeAsync(
        AcquisitionAnalysisBatch analysis,
        ScheduledFilterChange change,
        long firstNewRawSampleCounter,
        CancellationToken cancellationToken)
    {
        var warmup = change.Warmup;
        if (warmup is not null &&
            (warmup.LastSampleCounter + 1 != change.EffectiveFromRawSampleCounter ||
             firstNewRawSampleCounter != change.EffectiveFromRawSampleCounter))
        {
            // A raw gap occurred after the setting change; never carry IIR state across it.
            warmup = null;
        }

        var newSessionId = await CreateSessionAsync(analysis, change.Settings, change.Revision, warmup, cancellationToken);
        string? previousSessionId;
        var activated = false;
        lock (gate)
        {
            if (scheduledChange?.Revision != change.Revision)
            {
                // A newer user action superseded this one while local HTTP was running.
                previousSessionId = newSessionId;
            }
            else
            {
                previousSessionId = activeFilterSessionId;
                activeSettings = change.Settings;
                activeConfigurationRevision = change.Revision;
                activeFilterSessionId = newSessionId;
                scheduledChange = null;
                activated = true;
            }
        }

        if (previousSessionId is not null)
        {
            using var _ = await httpClient.DeleteAsync($"api/live-filters/sessions/{previousSessionId}", cancellationToken);
        }

        return activated;
    }

    private async Task<ActiveFilterSession> EnsureActiveSessionAsync(
        AcquisitionAnalysisBatch analysis,
        CancellationToken cancellationToken)
    {
        LiveDisplayFilterSettings settings;
        long revision;
        string? sessionId;
        lock (gate)
        {
            settings = activeSettings;
            revision = activeConfigurationRevision;
            sessionId = activeFilterSessionId;
        }
        if (sessionId is null)
        {
            sessionId = await CreateSessionAsync(analysis, settings, revision, null, cancellationToken);
            lock (gate)
            {
                activeFilterSessionId ??= sessionId;
                sessionId = activeFilterSessionId;
            }
        }

        return new ActiveFilterSession(sessionId, revision);
    }

    private async Task<string> CreateSessionAsync(
        AcquisitionAnalysisBatch analysis,
        LiveDisplayFilterSettings settings,
        long revision,
        LiveDisplayFilterWarmup? warmup,
        CancellationToken cancellationToken)
    {
        var eegIndexes = analysis.Stream.Channels
            .Where(channel => channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
            .Select(channel => channel.StreamIndex)
            .ToArray();
        var sessionId = $"{analysis.SessionId:N}-{revision}";
        using var response = await httpClient.PostAsJsonAsync("api/live-filters/sessions", new LiveFilterSessionRequest(
            sessionId,
            analysis.Stream.SamplingRateHz,
            analysis.Batch.ChannelCount,
            eegIndexes,
            settings.LowCutHz,
            settings.HighCutHz,
            settings.NotchHz,
            warmup?.SampleCount ?? 0,
            warmup?.SampleMajorValues ?? [],
            ReturnWarmupValues: false), cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LiveFilterSessionResponse>(cancellationToken)
            ?? throw new InvalidOperationException("实时滤波后端没有返回会话信息。");
        if (payload.WarmupSampleCount != (warmup?.SampleCount ?? 0) || payload.WarmupValuesV.Length != 0)
        {
            throw new InvalidOperationException("实时滤波预热数据契约无效。");
        }

        return sessionId;
    }

    private sealed record ScheduledFilterChange(
        LiveDisplayFilterSettings Settings,
        long Revision,
        long EffectiveFromRawSampleCounter,
        LiveDisplayFilterWarmup? Warmup);

    private sealed record ActiveFilterSession(string FilterSessionId, long ConfigurationRevision);

    private sealed record LiveFilterSessionRequest(
        [property: JsonPropertyName("session_id")] string SessionId,
        [property: JsonPropertyName("sampling_rate_hz")] int SamplingRateHz,
        [property: JsonPropertyName("channel_count")] int ChannelCount,
        [property: JsonPropertyName("eeg_channel_indexes")] int[] EegChannelIndexes,
        [property: JsonPropertyName("low_cut_hz")] double LowCutHz,
        [property: JsonPropertyName("high_cut_hz")] double HighCutHz,
        [property: JsonPropertyName("notch_hz")] double? NotchHz,
        [property: JsonPropertyName("warmup_sample_count")] int WarmupSampleCount,
        [property: JsonPropertyName("warmup_values_v")] double[] WarmupValuesV,
        [property: JsonPropertyName("return_warmup_values")] bool ReturnWarmupValues);

    private sealed record LiveFilterSessionResponse(
        [property: JsonPropertyName("warmup_sample_count")] int WarmupSampleCount,
        [property: JsonPropertyName("warmup_values_v")] double[] WarmupValuesV);

}

/// <summary>Preserves sample-major V values when a batch crosses a filter boundary.</summary>
public static class LiveFilterBatchSlicer
{
    public static AcquisitionBatch Slice(AcquisitionBatch source, int startSampleIndex, int sampleCount)
    {
        if (startSampleIndex < 0 || sampleCount <= 0 || startSampleIndex + sampleCount > source.SampleCount)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        }

        var values = new double[checked(sampleCount * source.ChannelCount)];
        Array.Copy(
            source.SampleMajorValues,
            startSampleIndex * source.ChannelCount,
            values,
            0,
            values.Length);
        return new AcquisitionBatch(
            source.FirstSampleCounter + startSampleIndex,
            sampleCount,
            source.ChannelCount,
            values,
            source.ReceivedAtUtc);
    }
}

public sealed record LiveDisplayFilterSchedule(
    long ConfigurationRevision,
    long EffectiveFromRawSampleCounter,
    long? SupersededPendingBoundary);

public sealed record FilteredDisplayBatch(
    Guid AcquisitionSessionId,
    long ConfigurationRevision,
    AcquisitionBatch Batch);
