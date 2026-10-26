using System.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.Diagnostics;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

/// <summary>One consumer's window observation; no algorithm arrays or playback clock.</summary>
public sealed class DynamicWindowObserver(IAlgorithmClient client)
{
    public async Task<DynamicWindowResponse> ObserveAsync(
        DynamicWindowRequest request, CancellationToken cancellationToken,
        Action<DynamicWindowResponse>? onStatus = null)
    {
        request = request with { RequestId = request.RequestId ?? Guid.NewGuid().ToString("N") };
        string? subscriptionId = null;
        var started = Stopwatch.GetTimestamp();
        var stateStarted = started;
        var pollCount = 0;
        try
        {
            RecordObservation(request, null, "submitting", "ensure_start", pollCount, started, null);
            var response = await client.EnsureDynamicWindowAsync(request, cancellationToken);
            subscriptionId = response.SubscriptionId;
            stateStarted = Stopwatch.GetTimestamp();
            RecordObservation(request, response.WindowId, response.State, "ensure_response", pollCount, started, null);
            if (response.State == "cancelled" && response.Failure?.Code == "WINDOW_NO_ACTIVE_SUBSCRIBERS")
            {
                // Seeking back to a released target explicitly requests a new
                // generation. The backend never silently reopens the old one.
                if (subscriptionId is not null)
                    await client.CancelDynamicSubscriptionAsync(subscriptionId, request.ProjectId, cancellationToken);
                subscriptionId = null;
                response = await client.EnsureDynamicWindowAsync(request with
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Retry = true,
                    RetryOfWindowId = response.WindowId
                }, cancellationToken);
                subscriptionId = response.SubscriptionId;
                stateStarted = Stopwatch.GetTimestamp();
                RecordObservation(request, response.WindowId, response.State, "retry_response", pollCount, started, null);
            }
            if (subscriptionId is null && IsPending(response.State))
            {
                // Older opt-in servers create subscriptions through a separate endpoint.
                subscriptionId = (await client.SubscribeDynamicWindowAsync(
                    response.WindowId, request.RequestId ?? Guid.NewGuid().ToString("N"),
                    request.ProjectId, cancellationToken)).SubscriptionId;
            }
            onStatus?.Invoke(response);
            var observedState = response.State;
            while (IsPending(response.State))
            {
                // A warm source can finish immediately after ensure. Discover
                // short jobs promptly, but back off for long source preparation.
                var delay = PollDelay(response.State, Stopwatch.GetElapsedTime(started),
                    Stopwatch.GetElapsedTime(stateStarted));
                await Task.Delay(delay, cancellationToken);
                response = await client.GetDynamicWindowAsync(response.WindowId, request.ProjectId, cancellationToken);
                pollCount++;
                var changed = response.State != observedState;
                if (changed)
                {
                    observedState = response.State;
                    stateStarted = Stopwatch.GetTimestamp();
                }
                RecordObservation(request, response.WindowId, response.State, changed ? "state_transition" : "poll",
                    pollCount, started, delay);
                onStatus?.Invoke(response);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return response;
        }
        finally
        {
            if (subscriptionId is not null)
            {
                // Cleanup has its own bounded token; cancelling a page must still release
                // only its subscription, even if transport cancellation is already set.
                using var release = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await client.CancelDynamicSubscriptionAsync(subscriptionId, request.ProjectId, release.Token); }
                catch (Exception error) { DesktopLog.Warning($"window_subscription_release_failed type={error.GetType().Name}"); }
            }
        }
    }

    private static bool IsPending(string state) => state is "queued" or "preparing_source" or "computing_window";

    internal static TimeSpan PollDelay(string state, TimeSpan elapsed, TimeSpan stateElapsed)
    {
        if (elapsed < TimeSpan.FromSeconds(1)) return TimeSpan.FromMilliseconds(100);
        if (stateElapsed >= TimeSpan.FromSeconds(3)) return TimeSpan.FromMilliseconds(750);
        return state switch
        {
            "queued" => TimeSpan.FromMilliseconds(500),
            "preparing_source" => TimeSpan.FromMilliseconds(250),
            "computing_window" => TimeSpan.FromMilliseconds(150),
            _ => TimeSpan.FromMilliseconds(500),
        };
    }

    private static void RecordObservation(DynamicWindowRequest request, string? windowId, string state,
        string stage, int pollCount, long started, TimeSpan? pollDelay) =>
        AlgorithmWindowPerformanceDiagnostics.RecordObservation(
            request, windowId, state, stage, pollCount,
            AlgorithmWindowPerformanceDiagnostics.ElapsedMilliseconds(started),
            pollDelay is null ? null : (long)pollDelay.Value.TotalMilliseconds);
}
