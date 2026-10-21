using System.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

/// <summary>One consumer's window observation; no algorithm arrays or playback clock.</summary>
public sealed class DynamicWindowObserver(IAlgorithmClient client)
{
    public async Task<DynamicWindowResponse> ObserveAsync(
        DynamicWindowRequest request, CancellationToken cancellationToken,
        Action<DynamicWindowResponse>? onStatus = null)
    {
        string? subscriptionId = null;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await client.EnsureDynamicWindowAsync(request, cancellationToken);
            subscriptionId = response.SubscriptionId;
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
            }
            if (subscriptionId is null && IsPending(response.State))
            {
                // Older opt-in servers create subscriptions through a separate endpoint.
                subscriptionId = (await client.SubscribeDynamicWindowAsync(
                    response.WindowId, request.RequestId ?? Guid.NewGuid().ToString("N"),
                    request.ProjectId, cancellationToken)).SubscriptionId;
            }
            onStatus?.Invoke(response);
            while (IsPending(response.State))
            {
                // A warm source can finish immediately after ensure. Discover
                // short jobs promptly, but back off for long source preparation.
                await Task.Delay(PollDelay(response.State, Stopwatch.GetElapsedTime(started)), cancellationToken);
                response = await client.GetDynamicWindowAsync(response.WindowId, request.ProjectId, cancellationToken);
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

    internal static TimeSpan PollDelay(string state, TimeSpan elapsed) => elapsed < TimeSpan.FromSeconds(1)
        ? TimeSpan.FromMilliseconds(100)
        : state switch
    {
        "queued" => TimeSpan.FromMilliseconds(500),
        "preparing_source" => TimeSpan.FromSeconds(1),
        "computing_window" => TimeSpan.FromMilliseconds(200),
        _ => TimeSpan.FromMilliseconds(500),
    };
}
