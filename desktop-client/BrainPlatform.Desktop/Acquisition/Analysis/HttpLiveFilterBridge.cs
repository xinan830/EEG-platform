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
    private PendingLiveFilterTransition? pendingTransition;
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

    public event EventHandler<LiveDisplayFilterActivated>? FilterConfigurationActivated;

    public event EventHandler<Exception>? FilterTransitionFailed;

    public void MarkUnavailable()
    {
        PendingLiveFilterTransition? abandoned;
        lock (gate)
        {
            isUnavailable = true;
            abandoned = pendingTransition;
            pendingTransition = null;
        }
        AbandonTransition(abandoned);
    }

    /// <summary>Sets the configuration that the next recording session starts with.</summary>
    public long Configure(LiveDisplayFilterSettings value)
    {
        value.Validate();
        PendingLiveFilterTransition? abandoned;
        long revision;
        lock (gate)
        {
            if (activeFilterSessionId is not null)
            {
                throw new InvalidOperationException("采集中请使用带时间边界的滤波切换。");
            }

            activeSettings = value;
            activeConfigurationRevision = revision = ++nextConfigurationRevision;
            abandoned = pendingTransition;
            pendingTransition = null;
            isUnavailable = false;
        }
        AbandonTransition(abandoned);
        return revision;
    }

    /// <summary>
    /// Schedules a display-only transition at the first sample after the raw
    /// counter observed when the user changed a control.
    /// </summary>
    public LiveDisplayFilterSchedule ScheduleChange(
        LiveDisplayFilterSettings value,
        long effectiveFromRawSampleCounter,
        IReadOnlyList<AcquisitionBatch> warmupBatches,
        int maximumWarmupSamples)
    {
        value.Validate();
        if (effectiveFromRawSampleCounter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveFromRawSampleCounter));
        }

        PendingLiveFilterTransition? superseded;
        LiveDisplayFilterSchedule schedule;
        lock (gate)
        {
            var revision = ++nextConfigurationRevision;
            var supersededPendingBoundary = pendingTransition?.RequestedFromRawSampleCounter;
            superseded = pendingTransition;
            pendingTransition = new PendingLiveFilterTransition(
                value,
                revision,
                effectiveFromRawSampleCounter,
                warmupBatches,
                maximumWarmupSamples);
            isUnavailable = false;
            schedule = new LiveDisplayFilterSchedule(
                revision,
                effectiveFromRawSampleCounter,
                supersededPendingBoundary);
        }
        AbandonTransition(superseded);
        return schedule;
    }

    public async Task PublishAsync(AcquisitionAnalysisBatch analysis, CancellationToken cancellationToken)
    {
        if (IsUnavailable)
        {
            return;
        }

        try
        {
            var transition = GetPendingTransition();
            if (transition is not null && analysis.Batch.LastSampleCounter >= transition.RequestedFromRawSampleCounter)
            {
                var transitionBatch = analysis.Batch;
                if (transitionBatch.FirstSampleCounter < transition.RequestedFromRawSampleCounter)
                {
                    var startSample = checked((int)(transition.RequestedFromRawSampleCounter - transitionBatch.FirstSampleCounter));
                    transitionBatch = LiveFilterBatchSlicer.Slice(
                        transitionBatch,
                        startSample,
                        transitionBatch.SampleCount - startSample);
                }

                transition.Enqueue(transitionBatch);
                transition.StartIfRequired(
                    () => RunTransitionAsync(analysis, transition),
                    cancellationToken);
            }

            // The active session remains the visible source while the new
            // session is created, warmed, and catches up in the background.
            await PublishActiveBatchAsync(analysis, analysis.Batch, cancellationToken);
            await TryActivateCaughtUpTransitionAsync(transition, analysis.Batch.LastSampleCounter);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            MarkUnavailable();

            throw;
        }
    }

    public async Task CloseAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        string? filterSessionId;
        PendingLiveFilterTransition? transition;
        lock (gate)
        {
            filterSessionId = activeFilterSessionId;
            activeFilterSessionId = null;
            transition = pendingTransition;
            pendingTransition = null;
            transition?.Cancel();
            isUnavailable = false;
        }
        if (transition is not null)
        {
            await transition.ObserveCompletionAsync();
            if (transition.FilterSessionId is { } pendingSessionId)
            {
                await CloseSessionBestEffortAsync(pendingSessionId, cancellationToken);
            }
        }
        if (filterSessionId is null)
        {
            return;
        }

        await CloseSessionBestEffortAsync(filterSessionId, cancellationToken);
    }

    private PendingLiveFilterTransition? GetPendingTransition()
    {
        lock (gate)
        {
            return pendingTransition;
        }
    }

    private async Task PublishActiveBatchAsync(
        AcquisitionAnalysisBatch analysis,
        AcquisitionBatch batch,
        CancellationToken cancellationToken)
    {
        var session = await EnsureActiveSessionAsync(analysis, cancellationToken);
        var filteredValues = await FilterBatchAsync(session.FilterSessionId, batch, cancellationToken);

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

    private async Task<double[]> FilterBatchAsync(
        string filterSessionId,
        AcquisitionBatch batch,
        CancellationToken cancellationToken)
    {
        var requestPayload = MemoryMarshal.AsBytes(batch.SampleMajorValues.AsSpan()).ToArray();
        using var content = new ByteArrayContent(requestPayload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.brain-platform.float64");
        using var response = await httpClient.PostAsync(
            $"api/live-filters/sessions/{filterSessionId}/batches/binary?sample_count={batch.SampleCount}",
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
        return filteredValues;
    }

    private async Task RunTransitionAsync(
        AcquisitionAnalysisBatch analysis,
        PendingLiveFilterTransition transition)
    {
        try
        {
            var warmup = transition.CreateWarmup();
            var newSessionId = await CreateSessionAsync(
                analysis,
                transition.Settings,
                transition.Revision,
                warmup,
                transition.CancellationToken);
            transition.MarkSessionCreated(newSessionId);
            await foreach (var batch in transition.ReadBatchesAsync())
            {
                _ = await FilterBatchAsync(newSessionId, batch, transition.CancellationToken);
                transition.MarkProcessed(batch.LastSampleCounter);
            }
        }
        catch (OperationCanceledException) when (transition.CancellationToken.IsCancellationRequested)
        {
            // A newer user choice or acquisition shutdown superseded this preparation.
        }
        catch (Exception exception)
        {
            transition.MarkFailed(exception);
        }
    }

    private async Task TryActivateCaughtUpTransitionAsync(
        PendingLiveFilterTransition? transition,
        long activeProcessedThroughRawSampleCounter)
    {
        if (transition is null)
        {
            return;
        }

        if (transition.Failure is { } failure)
        {
            lock (gate)
            {
                if (ReferenceEquals(pendingTransition, transition))
                {
                    pendingTransition = null;
                }
            }
            AbandonTransition(transition);
            FilterTransitionFailed?.Invoke(this, failure);
            return;
        }

        if (transition.FilterSessionId is not { } newSessionId ||
            transition.ProcessedThroughRawSampleCounter < activeProcessedThroughRawSampleCounter)
        {
            return;
        }

        string? previousSessionId;
        var boundary = checked(activeProcessedThroughRawSampleCounter + 1L);
        lock (gate)
        {
            if (!ReferenceEquals(pendingTransition, transition))
            {
                return;
            }

            previousSessionId = activeFilterSessionId;
            activeSettings = transition.Settings;
            activeConfigurationRevision = transition.Revision;
            activeFilterSessionId = newSessionId;
            pendingTransition = null;
            transition.Complete();
        }

        FilterConfigurationActivated?.Invoke(this, new LiveDisplayFilterActivated(
            transition.Revision,
            boundary,
            transition.WarmupSampleCount));
        if (previousSessionId is not null)
        {
            await CloseSessionBestEffortAsync(previousSessionId, CancellationToken.None);
        }
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
        var sessionCreated = false;
        try
        {
            using var response = await httpClient.PostAsJsonAsync("api/live-filters/sessions", new LiveFilterSessionRequest(
                sessionId,
                analysis.Stream.SamplingRateHz,
                analysis.Batch.ChannelCount,
                eegIndexes,
                settings.LowCutHz,
                settings.HighCutHz,
                settings.NotchHz), cancellationToken);
            response.EnsureSuccessStatusCode();
            sessionCreated = true;
            var payload = await response.Content.ReadFromJsonAsync<LiveFilterSessionResponse>(cancellationToken)
                ?? throw new InvalidOperationException("实时滤波后端没有返回会话信息。");
            if (payload.WarmupSampleCount != 0 || payload.WarmupValuesV.Length != 0)
            {
                throw new InvalidOperationException("实时滤波预热数据契约无效。");
            }

            if (warmup is not null)
            {
                var warmupPayload = MemoryMarshal.AsBytes(warmup.SampleMajorValues.AsSpan()).ToArray();
                using var warmupContent = new ByteArrayContent(warmupPayload);
                warmupContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.brain-platform.float64");
                using var warmupResponse = await httpClient.PostAsync(
                    $"api/live-filters/sessions/{sessionId}/warmup/binary?sample_count={warmup.SampleCount}",
                    warmupContent,
                    cancellationToken);
                warmupResponse.EnsureSuccessStatusCode();
                if (!warmupResponse.Headers.TryGetValues("X-Warmup-Sample-Count", out var counts) ||
                    !int.TryParse(counts.SingleOrDefault(), out var returnedCount) ||
                    returnedCount != warmup.SampleCount)
                {
                    throw new InvalidOperationException("实时滤波二进制预热数据契约无效。");
                }
            }

            return sessionId;
        }
        catch
        {
            if (sessionCreated)
            {
                await CloseSessionBestEffortAsync(sessionId, CancellationToken.None);
            }
            throw;
        }
    }

    private async Task CloseSessionBestEffortAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await httpClient.DeleteAsync($"api/live-filters/sessions/{sessionId}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Live filtering is transient; raw acquisition must never depend on cleanup.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown cancellation must not hold the device lifecycle open.
        }
    }

    private void AbandonTransition(PendingLiveFilterTransition? transition)
    {
        if (transition is null)
        {
            return;
        }

        transition.Cancel();
        _ = DisposeTransitionAsync(transition);
    }

    private async Task DisposeTransitionAsync(PendingLiveFilterTransition transition)
    {
        await transition.ObserveCompletionAsync();
        if (transition.FilterSessionId is { } sessionId)
        {
            await CloseSessionBestEffortAsync(sessionId, CancellationToken.None);
        }
    }

    private sealed record ActiveFilterSession(string FilterSessionId, long ConfigurationRevision);

    private sealed record LiveFilterSessionRequest(
        [property: JsonPropertyName("session_id")] string SessionId,
        [property: JsonPropertyName("sampling_rate_hz")] int SamplingRateHz,
        [property: JsonPropertyName("channel_count")] int ChannelCount,
        [property: JsonPropertyName("eeg_channel_indexes")] int[] EegChannelIndexes,
        [property: JsonPropertyName("low_cut_hz")] double LowCutHz,
        [property: JsonPropertyName("high_cut_hz")] double HighCutHz,
        [property: JsonPropertyName("notch_hz")] double? NotchHz);

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

public sealed record LiveDisplayFilterActivated(
    long ConfigurationRevision,
    long EffectiveFromRawSampleCounter,
    int WarmupSampleCount);

public sealed record FilteredDisplayBatch(
    Guid AcquisitionSessionId,
    long ConfigurationRevision,
    AcquisitionBatch Batch);
