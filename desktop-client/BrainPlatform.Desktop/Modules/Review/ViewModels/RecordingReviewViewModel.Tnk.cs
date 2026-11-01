using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.Diagnostics;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed partial class RecordingReviewViewModel
{
    private ReviewAnalysisCoordinator? tnkEventCoordinator;
    private Action<DynamicWindowResponse>? tnkWindowHandler;
    private Action<TnkPlaybackGap>? tnkGapHandler;
    private long tnkSubscriptionRevision;

    private void SubscribeTnkPlaybackEvents(ReviewAnalysisCoordinator coordinator)
    {
        UnsubscribeTnkPlaybackEvents();
        if (coordinator.AlgorithmId != "tnk_biofeedback") return;
        tnkEventCoordinator = coordinator;
        var revision = analysisConfigurationRevision;
        tnkSubscriptionRevision = revision;
        tnkWindowHandler = response =>
        {
            var dispatchStarted = Stopwatch.GetTimestamp();
            DispatchTnkEvent(() =>
            {
                var reason = !IsCurrentTnkSubscription(coordinator, revision) ? "stale_subscription"
                    : !coordinator.IsCurrentTnkEvent(response) ? "stale_delivery"
                    : response.Identity is not { ValueKind: System.Text.Json.JsonValueKind.Object } identity ? "missing_identity"
                    : !identity.TryGetProperty("recording_id", out var id) || id.GetString() != analysisRecording?.Id
                        ? "recording_mismatch"
                        : null;
                var range = response.Range;
                var context = $" sequence={EventSequence(response.RequestId)} window_id={response.WindowId}" +
                    $" start_sample={range?.StartSample.ToString() ?? "none"}" +
                    $" end_sample={range?.EndSample.ToString() ?? "none"}" +
                    $" dispatcher_wait_ms={AlgorithmWindowPerformanceDiagnostics.ElapsedMilliseconds(dispatchStarted)}";
                if (reason is not null)
                {
                    DesktopLog.Warning($"review_tnk_frame_ui_dropped reason={reason}{context}");
                    return;
                }
                var added = TnkAnalysis.Apply(response, AnalysisPanelStartSeconds);
                DesktopLog.Info($"review_tnk_frame_ui_applied history_added={added}{context}");
            });
        };
        tnkGapHandler = gap => DispatchTnkEvent(() =>
        {
            if (IsCurrentTnkSubscription(coordinator, revision) && coordinator.IsCurrentTnkEvent(gap))
                TnkAnalysis.AddGap(gap, AnalysisPanelStartSeconds);
        });
        coordinator.TnkWindowCompleted += tnkWindowHandler;
        coordinator.TnkGapReported += tnkGapHandler;
    }

    private void UnsubscribeTnkPlaybackEvents()
    {
        if (tnkEventCoordinator is not null)
        {
            if (tnkWindowHandler is not null) tnkEventCoordinator.TnkWindowCompleted -= tnkWindowHandler;
            if (tnkGapHandler is not null) tnkEventCoordinator.TnkGapReported -= tnkGapHandler;
        }
        tnkEventCoordinator = null;
        tnkWindowHandler = null;
        tnkGapHandler = null;
    }

    private bool IsCurrentTnkSubscription(ReviewAnalysisCoordinator coordinator, long revision) =>
        ReferenceEquals(tnkEventCoordinator, coordinator) && ReferenceEquals(analysisCoordinator, coordinator) &&
        activeAnalysisId == "tnk_biofeedback" && tnkSubscriptionRevision == revision &&
        revision == analysisConfigurationRevision;

    private static void DispatchTnkEvent(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (dispatcher.CheckAccess()) action();
        else _ = dispatcher.BeginInvoke(DispatcherPriority.Background, action);
    }

    private static string EventSequence(string requestId)
    {
        var separator = requestId.LastIndexOf(':');
        return separator >= 0 && separator < requestId.Length - 1 ? requestId[(separator + 1)..] : "unknown";
    }
}
