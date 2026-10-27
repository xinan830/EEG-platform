using System.Diagnostics;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel
{
    private string? tnkPlaybackDeliveryId;
    private CancellationTokenSource? tnkPlaybackEventsCancellation;
    private readonly TnkPlaybackDeliveryDiagnostics tnkPlaybackDeliveryDiagnostics = new();
    private long tnkPlaybackEventSequence;
    private long? tnkPlaybackLastRequestedSample;

    private async Task ConsumeTnkPlaybackEventsAsync(string? projectId, string? deliveryId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deliveryId)) return;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var pollStarted = Stopwatch.GetTimestamp();
                var batch = await client.GetTnkPlaybackEventsAsync(
                    deliveryId, tnkPlaybackEventSequence, projectId, cancellationToken).ConfigureAwait(false);
                tnkPlaybackDeliveryDiagnostics.Record(pollStarted, Stopwatch.GetTimestamp(),
                    batch.Events.Count, batch.HasMore, batch.Coverage == "incomplete");
                if (cancellationToken.IsCancellationRequested || deliveryId != tnkPlaybackDeliveryId)
                    return;
                if (batch.Coverage == "incomplete")
                {
                    DesktopLog.Warning($"tnk_playback_delivery_gap delivery_id={deliveryId} first={batch.Discontinuity?.FirstMissingSequence} last={batch.Discontinuity?.LastMissingSequence}");
                    PublishTnkPlaybackGap(batch.Discontinuity, deliveryId);
                    if (batch.Discontinuity is { SampleRangeExact: true } missing &&
                        missing.AffectedEndSample > missing.AffectedStartSample &&
                        windowRequest is { } request && windowHandler is { } handler)
                    {
                        // Re-ensure the canonical target at the end of the lost range.
                        // TNK replay remains ordered in the backend and republishes recovered frames.
                        _ = ObservePlaybackTargetAsync(new DynamicWindowRequest(request.RecordingId,
                            handler.AlgorithmId, null, null, request.Config, missing.AffectedEndSample,
                            RequestId: Guid.NewGuid().ToString("N"), PlaybackDelivery: true,
                            PlaybackDeliveryId: deliveryId));
                    }
                    if (batch.Discontinuity is { } discontinuity)
                        tnkPlaybackEventSequence = Math.Max(tnkPlaybackEventSequence, discontinuity.LastMissingSequence);
                }
                foreach (var item in batch.Events.OrderBy(item => item.DeliverySequence))
                {
                    if (item.DeliverySequence <= tnkPlaybackEventSequence) continue;
                    tnkPlaybackEventSequence = item.DeliverySequence;
                    PublishTnkPlaybackFrame(item);
                }
                if (batch.Events.Count == 0 || !batch.HasMore)
                    await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (AlgorithmApiException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // The event consumer starts before the first target request
                // creates the server buffer. Keep polling through that gap.
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
            }
            catch (NotSupportedException error)
            {
                DesktopLog.Warning($"tnk_playback_delivery_unsupported type={error.GetType().Name}");
                return;
            }
            catch (Exception error)
            {
                DesktopLog.Warning($"tnk_playback_event_poll_failed type={error.GetType().Name}");
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void PublishTnkPlaybackGap(TnkPlaybackDiscontinuityResponse? discontinuity, string deliveryId)
    {
        var samplingRate = RegisteredRecording?.SamplingRateHz;
        if (discontinuity is null || samplingRate is not > 0 || deliveryId != tnkPlaybackDeliveryId)
            return;
        var gap = new TnkPlaybackGap(
            discontinuity.FirstMissingSequence, discontinuity.LastMissingSequence,
            discontinuity.AffectedStartSample, discontinuity.AffectedEndSample,
            discontinuity.AffectedStartSample / samplingRate.Value,
            discontinuity.AffectedEndSample / samplingRate.Value,
            discontinuity.SampleRangeExact, deliveryId);
        PublishOwnedTnkPlaybackGap(gap);
    }

    private void PublishOwnedTnkPlaybackGap(TnkPlaybackGap gap)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        void Publish() => OwnedTnkPlaybackGap?.Invoke(gap);
        if (dispatcher.CheckAccess()) Publish();
        else _ = dispatcher.BeginInvoke(Publish);
    }

    private void PublishTnkPlaybackRecoveryFailed(string deliveryId)
    {
        if (deliveryId != tnkPlaybackDeliveryId) return;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        void Publish()
        {
            if (deliveryId == tnkPlaybackDeliveryId)
                OwnedTnkPlaybackRecoveryFailed?.Invoke(deliveryId);
        }
        if (dispatcher.CheckAccess()) Publish();
        else _ = dispatcher.BeginInvoke(Publish);
    }

    private void PublishTnkPlaybackFrame(TnkPlaybackFrameEventResponse item)
    {
        if (item.DeliveryId != tnkPlaybackDeliveryId ||
            !string.Equals(item.WindowIdKind, "scientific_identity_key", StringComparison.Ordinal)) return;
        var range = new DynamicWindowRange(
            item.Identity.GetProperty("start_sample").GetInt64(),
            item.Identity.GetProperty("end_sample").GetInt64(),
            item.WindowStartSeconds, item.WindowEndSeconds);
        var plan = new DynamicWindowPlanResponse(range.EndSample, range.StartSample, range.EndSample,
            range.StartSeconds, range.EndSeconds, "planned", null);
        var response = new DynamicWindowResponse(item.SessionId, item.WindowId,
            $"tnk-event-{item.DeliverySequence}", item.Result.State, item.Identity,
            plan, range, item.Result.Quality, null, item.Result);
        var request = windowRequest;
        if (AlgorithmWindowPerformanceDiagnostics.Enabled && request is not null)
        {
            var samplingRate = RegisteredRecording?.SamplingRateHz ?? item.SamplingRateHz;
            var cursorSample = tnkPlaybackLastRequestedSample ?? range.EndSample;
            var lagSeconds = (cursorSample - range.EndSample) / samplingRate;
            var deliveryAgeMs = item.PublishedAtUtc is { } published
                ? (DateTimeOffset.UtcNow - published).TotalMilliseconds : (double?)null;
            DesktopLog.Info($"tnk_playback_event_delivery scenario={DesktopLog.PerformanceScenario}" +
                $" recording_id={request.RecordingId} delivery_id={item.DeliveryId}" +
                $" sequence={item.DeliverySequence} window_end_sample={range.EndSample}" +
                $" cursor_sample={cursorSample} result_lag_s={lagSeconds:0.###}" +
                (deliveryAgeMs is double age ? $" delivery_age_ms={age:0.0}" : " delivery_age_ms=unknown"));
        }
        if (request is not null) PublishOwnedWindowCompleted(
            new DynamicWindowRequest(request.RecordingId, "tnk_biofeedback", null, null,
                request.Config, range.EndSample,
                RequestId: $"playback-event-{item.DeliverySequence}",
                PlaybackDelivery: true, PlaybackDeliveryId: item.DeliveryId), response);
    }

    private void ResetTnkPlaybackDeliveryForBackwardSeek(long cursorSample)
    {
        if (tnkPlaybackDeliveryId is null || tnkPlaybackLastRequestedSample is not long previous ||
            cursorSample >= previous) return;
        CloseTnkPlaybackDelivery();
        tnkPlaybackDeliveryId = Guid.NewGuid().ToString("N");
        tnkPlaybackEventSequence = 0;
        lastRequestedWindowSample = -1;
        lastPlaybackWindowSample = -1;
        tnkPlaybackEventsCancellation = new CancellationTokenSource();
        _ = ConsumeTnkPlaybackEventsAsync(null, tnkPlaybackDeliveryId,
            tnkPlaybackEventsCancellation.Token);
        tnkPlaybackLastRequestedSample = null;
    }

    private void CloseTnkPlaybackDelivery()
    {
        var deliveryId = tnkPlaybackDeliveryId;
        tnkPlaybackEventsCancellation?.Cancel();
        tnkPlaybackEventsCancellation?.Dispose();
        tnkPlaybackEventsCancellation = null;
        if (deliveryId is not null) _ = CloseTnkPlaybackDeliveryAsync(deliveryId);
    }

    private async Task CloseTnkPlaybackDeliveryAsync(string deliveryId)
    {
        try
        {
            await client.CloseTnkPlaybackEventsAsync(deliveryId, null, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException) { }
        catch (Exception error)
        {
            DesktopLog.Warning($"tnk_playback_delivery_close_failed type={error.GetType().Name}");
        }
    }
}
