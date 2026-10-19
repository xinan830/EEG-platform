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
                // Source preparation can take seconds for long recordings. Do
                // not poll it at the computation cadence; keep the faster
                // cadence only once the target window is actually computing.
                await Task.Delay(PollDelay(response.State), cancellationToken);
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

    private static TimeSpan PollDelay(string state) => state switch
    {
        "queued" => TimeSpan.FromMilliseconds(500),
        "preparing_source" => TimeSpan.FromSeconds(1),
        "computing_window" => TimeSpan.FromMilliseconds(200),
        _ => TimeSpan.FromMilliseconds(500),
    };
}
