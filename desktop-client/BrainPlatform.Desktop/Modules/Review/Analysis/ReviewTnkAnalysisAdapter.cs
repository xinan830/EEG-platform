using System.Net;
using System.Text.Json;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Review-owned TNK delivery session over the existing ordered event API.</summary>
internal sealed class ReviewTnkAnalysisAdapter(IAlgorithmClient client) :
    IReviewAlgorithmAdapter, IReviewTnkPlaybackEvents
{
    private const string EventRunPrefix = "review-tnk-event:";
    private const int MaximumPendingFrames = 600;
    private readonly DynamicWindowTargetCoordinator targets = new(client);
    private readonly object gate = new();
    private readonly List<TnkPlaybackFrameEventResponse> pendingFrames = [];
    private readonly HashSet<string> publishedWindowIds = new(StringComparer.Ordinal);
    private TnkPlaybackGap? pendingOverflowGap;
    private CancellationTokenSource? eventCancellation;
    private Task? eventConsumer;
    private string? deliveryId;
    private ReviewAnalysisSnapshot? snapshot;
    private string? configurationSha256;
    private RegisteredRecording? recording;
    private long generation;
    private long eventSequence;
    private long lastCursorSample = -1;

    public string AlgorithmId => "tnk_biofeedback";
    public string DisplayName => "TNK 脑指标";
    public bool SupportsOnDemandWindows => OfflineAnalysisAvailability.IsOnDemandEnabled(AlgorithmId, true);
    public event Action<DynamicWindowResponse>? WindowCompleted;
    public event Action<TnkPlaybackGap>? GapReported;

    public bool IsCurrent(DynamicWindowResponse response)
    {
        lock (gate) return deliveryId is not null &&
            response.RequestId.StartsWith($"{EventRunPrefix}{deliveryId}:", StringComparison.Ordinal);
    }

    public bool IsCurrent(TnkPlaybackGap gap)
    {
        lock (gate) return deliveryId is not null && gap.DeliveryId == deliveryId;
    }

    public async Task<RegisteredRecording> RegisterAsync(string directory, CancellationToken cancellationToken,
        IReadOnlyList<MontageProfile>? montageProfiles = null)
    {
        recording = await (client as IRecordingRegistrationClient ??
            throw new InvalidOperationException("算法客户端不支持记录注册。"))
            .RegisterWpfRecordingAsync(directory, cancellationToken, montageProfiles);
        return recording;
    }

    public async Task<AlgorithmCatalogItem> GetAlgorithmAsync(CancellationToken cancellationToken)
    {
        var algorithm = (await client.ListAlgorithmsAsync(cancellationToken))
            .SingleOrDefault(item => item.Id == AlgorithmId);
        if (algorithm is null || !algorithm.IsRunnable || !algorithm.SupportsDeclaredOnDemand)
            throw new NotSupportedException("当前 TNK 未声明可运行的按需动态窗口能力。");
        return algorithm;
    }

    public Task<AnalysisRunResponse> EnsureRunAsync(AnalysisRunRequest request,
        Action<AnalysisRunResponse>? onStatusChanged, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Review TNK 面板只使用有序按需窗口，不创建完整 Run。");

    public Task<WindowPreviewResponse> LoadAsync(string recordingId, string runId, long cursorSample,
        CancellationToken cancellationToken) => throw new NotSupportedException("Review TNK 面板不读取完整 Run。");

    public Task<WindowPreviewResponse> LoadOnDemandAsync(string recordingId, AnalysisRunRequest request,
        long cursorSample, CancellationToken cancellationToken) =>
        throw new NotSupportedException("TNK Review 必须使用带不可变身份的有序窗口请求。");

    public async Task<WindowPreviewResponse> LoadOnDemandAsync(string recordingId,
        ReviewAnalysisSnapshot requestSnapshot, long cursorSample, CancellationToken cancellationToken,
        IProgress<DynamicWindowResponse>? progress)
    {
        if (requestSnapshot.AlgorithmId != AlgorithmId || requestSnapshot.RecordingId != recordingId || cursorSample < 0)
            throw new ArgumentException("TNK 请求快照与当前 Review 记录不匹配。", nameof(requestSnapshot));

        var context = EnsureDelivery(requestSnapshot, cursorSample);
        var response = await targets.LoadAsync(new DynamicWindowRequest(recordingId, AlgorithmId,
            requestSnapshot.DefinitionId, requestSnapshot.DefinitionVersion, requestSnapshot.Configuration,
            cursorSample, RequestId: Guid.NewGuid().ToString("N"), PlaybackDelivery: true,
            PlaybackDeliveryId: context.DeliveryId, PlaybackGeneration: context.Generation,
            AnalysisContextGeneration: context.Generation), cancellationToken, progress);
        ReviewWindowAnalysisAdapter.ValidateSnapshotIdentity(response, requestSnapshot);
        await BindConfiguration(response, requestSnapshot, context);
        return ReviewWindowAnalysisAdapter.ToWindowPreview(response, recordingId, cursorSample);
    }

    public async Task ReleaseOnDemandAsync(CancellationToken cancellationToken = default)
    {
        var release = TakeDelivery();
        await targets.ResetAsync().WaitAsync(cancellationToken);
        if (release.Consumer is not null)
        {
            try { await release.Consumer.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (release.Cancellation?.IsCancellationRequested == true) { }
        }
        release.Cancellation?.Dispose();
        if (release.DeliveryId is not null)
        {
            try { await client.CloseTnkPlaybackEventsAsync(release.DeliveryId, null, cancellationToken); }
            catch (AlgorithmApiException error) when (error.StatusCode == HttpStatusCode.NotFound) { }
            catch (Exception error)
            {
                DesktopLog.Warning($"review_tnk_delivery_close_failed type={error.GetType().Name}");
            }
        }
    }

    private (string DeliveryId, long Generation) EnsureDelivery(ReviewAnalysisSnapshot next, long cursorSample)
    {
        string? closeId = null;
        CancellationTokenSource? oldCancellation = null;
        Task? oldConsumer = null;
        lock (gate)
        {
            var changed = snapshot is not null &&
                (snapshot.RecordingId != next.RecordingId || snapshot.SourceSha256 != next.SourceSha256 ||
                 snapshot.AlgorithmId != next.AlgorithmId || snapshot.DefinitionId != next.DefinitionId ||
                 snapshot.DefinitionVersion != next.DefinitionVersion ||
                 snapshot.ImplementationIdentity != next.ImplementationIdentity ||
                 !Equivalent(snapshot.Configuration, next.Configuration));
            if (changed || (lastCursorSample >= 0 && cursorSample < lastCursorSample))
            {
                closeId = deliveryId;
                oldCancellation = eventCancellation;
                oldConsumer = eventConsumer;
                eventCancellation = null;
                eventConsumer = null;
                deliveryId = null;
                configurationSha256 = null;
                pendingFrames.Clear();
                pendingOverflowGap = null;
                publishedWindowIds.Clear();
                eventSequence = 0;
                generation++;
            }
            if (deliveryId is null)
            {
                deliveryId = Guid.NewGuid().ToString("N");
                eventCancellation = new CancellationTokenSource();
                var token = eventCancellation.Token;
                var id = deliveryId;
                eventConsumer = ConsumeEventsAsync(id, generation, next, token);
            }
            if (snapshot?.ScientificConfiguration is null || next.ScientificConfiguration is not null)
                snapshot = next;
            lastCursorSample = cursorSample;
            recording = recording is { } registered && registered.Id == next.RecordingId ? registered : null;
            var result = (deliveryId, generation);
            if (oldCancellation is not null)
            {
                oldCancellation.Cancel();
                _ = CloseOldDeliveryAsync(closeId, oldCancellation, oldConsumer);
            }
            return result;
        }
    }

    private Task BindConfiguration(DynamicWindowResponse response, ReviewAnalysisSnapshot expected,
        (string DeliveryId, long Generation) context)
    {
        if (response.Identity is not { ValueKind: JsonValueKind.Object } identity ||
            !identity.TryGetProperty("configuration_sha256", out var digest) || digest.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("TNK 窗口缺少科学配置摘要，不能接收事件流。");
        var bound = expected.BindScientificConfiguration(response.ScientificConfigurationJson ?? "", digest.GetString()!);
        TnkPlaybackFrameEventResponse[] queued;
        TnkPlaybackGap? overflowGap;
        string? previousDigest;
        lock (gate)
        {
            if (deliveryId != context.DeliveryId || generation != context.Generation || snapshot?.RecordingId != expected.RecordingId)
                return Task.CompletedTask;
            previousDigest = configurationSha256;
            snapshot = bound;
            configurationSha256 = digest.GetString();
            queued = pendingFrames.ToArray();
            pendingFrames.Clear();
            overflowGap = pendingOverflowGap;
            pendingOverflowGap = null;
        }
        if (previousDigest is not null && previousDigest != digest.GetString())
            DesktopLog.Warning($"review_tnk_configuration_changed delivery_id={context.DeliveryId}" +
                $" previous={DigestPrefix(previousDigest)} current={DigestPrefix(digest.GetString()!)}");
        foreach (var frame in queued.OrderBy(item => item.DeliverySequence)) PublishFrame(frame, context, bound);
        if (overflowGap is not null) GapReported?.Invoke(overflowGap);
        return Task.CompletedTask;
    }

    private async Task ConsumeEventsAsync(string id, long ownerGeneration, ReviewAnalysisSnapshot owner,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var batch = await client.GetTnkPlaybackEventsAsync(id, Volatile.Read(ref eventSequence), null,
                    cancellationToken).ConfigureAwait(false);
                if (!IsCurrent(id, ownerGeneration, cancellationToken)) return;
                if (batch.Coverage == "incomplete" && batch.Discontinuity is { } missing)
                    PublishGap(id, ownerGeneration, missing, owner);
                foreach (var frame in batch.Events.OrderBy(item => item.DeliverySequence))
                {
                    if (!IsCurrent(id, ownerGeneration, cancellationToken)) return;
                    if (frame.DeliverySequence <= Volatile.Read(ref eventSequence)) continue;
                    Interlocked.Exchange(ref eventSequence, frame.DeliverySequence);
                    DesktopLog.Info($"review_tnk_frame_received delivery_id={id}" +
                        $" sequence={frame.DeliverySequence} window_id={frame.WindowId}" +
                        $" start_sample={frame.Identity.GetProperty("start_sample").GetInt64()}" +
                        $" end_sample={frame.Identity.GetProperty("end_sample").GetInt64()}" +
                        $" state={frame.Result.State} quality={frame.Result.Quality}");
                    PublishOrQueueFrame(frame, id, ownerGeneration, owner);
                }
                if (batch.Delivery is { State: "failure", FailedStartSample: long start, FailedEndSample: long end } && end > start)
                {
                    var rate = recording?.SamplingRateHz ?? 0;
                    if (rate > 0) GapReported?.Invoke(new(batch.LatestSequence + 1, batch.LatestSequence + 1,
                        start, end, start / rate, end / rate, true, id, batch.Delivery.Code));
                }
                if (batch.Events.Count == 0 || !batch.HasMore)
                    await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (AlgorithmApiException error) when (error.StatusCode == HttpStatusCode.NotFound)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
            }
            catch (NotSupportedException) { return; }
            catch (Exception error)
            {
                DesktopLog.Warning($"review_tnk_event_poll_failed type={error.GetType().Name}");
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void PublishOrQueueFrame(TnkPlaybackFrameEventResponse frame, string id, long ownerGeneration,
        ReviewAnalysisSnapshot owner)
    {
        ReviewAnalysisSnapshot? bound;
        bool configurationBound;
        lock (gate)
        {
            if (deliveryId != id || generation != ownerGeneration) return;
            bound = snapshot;
            configurationBound = configurationSha256 is not null;
            if (!configurationBound)
            {
                if (pendingFrames.Count < MaximumPendingFrames) pendingFrames.Add(frame);
                else
                {
                    var start = frame.Identity.GetProperty("start_sample").GetInt64();
                    var end = frame.Identity.GetProperty("end_sample").GetInt64();
                    var rate = recording?.SamplingRateHz ?? frame.SamplingRateHz;
                    pendingOverflowGap = pendingOverflowGap is { } previous
                        ? previous with { LastMissingSequence = frame.DeliverySequence,
                            AffectedStartSample = Math.Min(previous.AffectedStartSample, start),
                            AffectedEndSample = Math.Max(previous.AffectedEndSample, end),
                            StartSeconds = Math.Min(previous.StartSeconds, start / rate),
                            EndSeconds = Math.Max(previous.EndSeconds, end / rate) }
                        : new(frame.DeliverySequence, frame.DeliverySequence, start, end,
                            start / rate, end / rate, true, id, "LOCAL_PENDING_EVENT_LIMIT");
                }
            }
        }
        if (!configurationBound) return;
        if (bound is not null) PublishFrame(frame, (id, ownerGeneration), bound);
    }

    private void PublishFrame(TnkPlaybackFrameEventResponse frame,
        (string DeliveryId, long Generation) context, ReviewAnalysisSnapshot owner)
    {
        if (!IsCurrent(context.DeliveryId, context.Generation, CancellationToken.None))
        {
            LogFrameRejected(frame, "stale_delivery");
            return;
        }
        var mismatches = new List<string>(5);
        if (!string.Equals(frame.DeliveryId, context.DeliveryId, StringComparison.Ordinal)) mismatches.Add("delivery_id");
        if (frame.WindowIdKind != "scientific_identity_key") mismatches.Add("window_id_kind");
        if (!Matches(frame.Identity, "recording_id", owner.RecordingId)) mismatches.Add("recording_id");
        if (!Matches(frame.Identity, "source_sha256", owner.SourceSha256)) mismatches.Add("source_sha256");
        if (!Matches(frame.Identity, "algorithm_id", AlgorithmId)) mismatches.Add("algorithm_id");
        var expectedDigest = owner.ScientificConfiguration?.Sha256 ?? "";
        if (!Matches(frame.Identity, "configuration_sha256", expectedDigest))
        {
            var actualDigest = frame.Identity.TryGetProperty("configuration_sha256", out var value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "missing";
            mismatches.Add($"configuration_sha256(actual={DigestPrefix(actualDigest)},expected={DigestPrefix(expectedDigest)})");
        }
        if (mismatches.Count > 0)
        {
            LogFrameRejected(frame, $"identity_mismatch fields={string.Join(',', mismatches)}");
            return;
        }
        string? rejection = null;
        lock (gate)
        {
            if (deliveryId != context.DeliveryId || generation != context.Generation)
                rejection = "stale_delivery";
            else if (!publishedWindowIds.Add(frame.WindowId)) rejection = "duplicate_window";
        }
        if (rejection is not null)
        {
            LogFrameRejected(frame, rejection);
            return;
        }
        var startSample = frame.Identity.GetProperty("start_sample").GetInt64();
        var endSample = frame.Identity.GetProperty("end_sample").GetInt64();
        DesktopLog.Info($"review_tnk_frame_published delivery_id={context.DeliveryId}" +
            $" sequence={frame.DeliverySequence} window_id={frame.WindowId}" +
            $" start_sample={startSample} end_sample={endSample}");
        var range = new DynamicWindowRange(startSample, endSample, frame.WindowStartSeconds, frame.WindowEndSeconds);
        var plan = new DynamicWindowPlanResponse(endSample, startSample, endSample,
            frame.WindowStartSeconds, frame.WindowEndSeconds, "planned", null);
        WindowCompleted?.Invoke(new DynamicWindowResponse(frame.SessionId, frame.WindowId,
            $"{EventRunPrefix}{context.DeliveryId}:{frame.DeliverySequence}", frame.Result.State, frame.Identity,
            plan, range, frame.Result.Quality, null, frame.Result));
    }

    private static void LogFrameRejected(TnkPlaybackFrameEventResponse frame, string reason) =>
        DesktopLog.Warning($"review_tnk_frame_rejected sequence={frame.DeliverySequence}" +
            $" window_id={frame.WindowId} reason={reason}");

    private void PublishGap(string id, long ownerGeneration, TnkPlaybackDiscontinuityResponse missing,
        ReviewAnalysisSnapshot owner)
    {
        if (!IsCurrent(id, ownerGeneration, CancellationToken.None)) return;
        var rate = recording?.SamplingRateHz ?? 0;
        if (rate <= 0) return;
        GapReported?.Invoke(new(missing.FirstMissingSequence, missing.LastMissingSequence,
            missing.AffectedStartSample, missing.AffectedEndSample,
            missing.AffectedStartSample / rate, missing.AffectedEndSample / rate,
            missing.SampleRangeExact, id));
    }

    private bool IsCurrent(string id, long ownerGeneration, CancellationToken token)
    {
        lock (gate) return !token.IsCancellationRequested && deliveryId == id && generation == ownerGeneration;
    }

    private async Task CloseOldDeliveryAsync(string? id, CancellationTokenSource cancellation, Task? consumer)
    {
        try { await (consumer ?? Task.CompletedTask); }
        catch (OperationCanceledException) { }
        cancellation.Dispose();
        if (id is not null)
        {
            try { await client.CloseTnkPlaybackEventsAsync(id, null, CancellationToken.None); }
            catch { }
        }
    }

    private (string? DeliveryId, CancellationTokenSource? Cancellation, Task? Consumer) TakeDelivery()
    {
        lock (gate)
        {
            var result = (DeliveryId: deliveryId, Cancellation: eventCancellation, Consumer: eventConsumer);
            deliveryId = null;
            eventCancellation = null;
            eventConsumer = null;
            snapshot = null;
            configurationSha256 = null;
            recording = null;
            lastCursorSample = -1;
            eventSequence = 0;
            pendingFrames.Clear();
            pendingOverflowGap = null;
            publishedWindowIds.Clear();
            generation++;
            result.Cancellation?.Cancel();
            return result;
        }
    }

    private static bool Matches(JsonElement identity, string key, string expected) =>
        identity.ValueKind == JsonValueKind.Object && identity.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String && value.GetString() == expected;

    private static string DigestPrefix(string value) => value.Length <= 12 ? value : value[..12];

    private static bool Equivalent(JsonElement left, JsonElement right) =>
        left.GetRawText() == right.GetRawText();
}
