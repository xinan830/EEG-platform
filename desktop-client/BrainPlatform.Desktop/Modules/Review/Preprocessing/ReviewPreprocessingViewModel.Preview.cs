using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Preprocessing;

/// <summary>Owns cancellable paired scientific viewport previews; stale or incomplete pairs never publish.</summary>
public sealed partial class ReviewPreprocessingViewModel
{
    private const double ScientificPreviewLowHz = 1.0;
    private const double ScientificPreviewHighHz = 50.0;
    private const double ScientificPreviewNotchHz = 50.0;
    private const double ScientificPreviewNotchQ = 30.0;
    private const int MinimumPointBudget = 1000;
    private const int MaximumPointBudget = 4000;

    private readonly ScientificComparisonTileCache previewTileCache = new();
    private CancellationTokenSource? prefetchCts;
    private string? inFlightForegroundKey;
    private string? inFlightPrefetchKey;
    private long overlayGeneration;
    private bool isPlaybackActive;
    private bool hasOverlayFailure;
    private int previewPointBudget = 1200;

    public bool IsDisplayMontageCompatible { get; private set; } = true;
    public int PreviewPointBudget => previewPointBudget;

    public bool IsScientificComparisonReady =>
        IsDisplayMontageCompatible &&
        IsOverlayCompareEnabled && !hasOverlayFailure && currentOverlayWindow is { } preview &&
        (getDisplayedChannels is null || MatchesDisplayedChannels(preview, getDisplayedChannels()));

    /// <summary>True whenever Scientific Comparison view mode is active and healthy (including while playing, scrolling,
    /// or loading tiles), keeping ordinary colored traces hidden so playback/scrolling never flashes back to color.</summary>
    public bool SuppressesOrdinaryColoredTraces(IReadOnlyList<string> displayedChannels) =>
        IsDisplayMontageCompatible &&
        IsOverlayCompareEnabled && !hasOverlayFailure &&
        ScientificInputBlockReason() is null &&
        previewPointBudget > 0 && displayedChannels.Count > 0 &&
        (currentOverlayWindow is null || MatchesDisplayedChannels(currentOverlayWindow, displayedChannels));

    public bool CanDisplayScientificComparison(IReadOnlyList<string> displayedChannels) =>
        IsScientificComparisonReady && MatchesDisplayedChannels(currentOverlayWindow!, displayedChannels);

    private static bool MatchesDisplayedChannels(
        PreprocessingPreviewWindowResponse preview, IReadOnlyList<string> displayedChannels) =>
        displayedChannels.Count > 0 &&
        displayedChannels.Distinct(StringComparer.OrdinalIgnoreCase).Count() == displayedChannels.Count &&
        displayedChannels.All(channel => preview.Baseline.Channels.Contains(channel, StringComparer.OrdinalIgnoreCase) ||
            preview.Processed.ExcludedOutputChannels.Contains(channel, StringComparer.OrdinalIgnoreCase));

    public string ScientificComparisonLegendText
    {
        get
        {
            if (currentOverlayWindow is not { } preview)
                return string.Empty;
            var filterNote = "零相位 1–50 Hz，陷波 50 Hz（Q=30）；顶部滤波仅用于彩色阅图";
            return preview.EnhancedPreprocessingActive
                ? $"科学对比：浅灰=基础科学滤波基线 · 深灰=科学处理后（{filterNote}）"
                : $"科学对比：仅基础科学滤波（{filterNote}；增强处理全关，前后一致）";
        }
    }

    public static int ComputePointBudget(double plotWidthDip) =>
        !double.IsFinite(plotWidthDip) || plotWidthDip <= 0
            ? 0
            : Math.Clamp((int)Math.Ceiling(plotWidthDip * 2.0), MinimumPointBudget, MaximumPointBudget);

    public void UpdatePlotWidth(double plotWidthDip)
    {
        var nextBudget = ComputePointBudget(plotWidthDip);
        if (nextBudget == previewPointBudget) return;
        previewPointBudget = nextBudget;
        RaisePropertyChanged(nameof(PreviewPointBudget));
        if (!IsOverlayCompareEnabled) return;
        if (nextBudget <= 0)
        {
            ClearOverlayWindow();
            OverlayStatusText = "等待绘图区尺寸就绪…";
            return;
        }
        _ = RefreshOverlayWindowAsync();
    }

    private void ClearOverlayWindow()
    {
        overlayGeneration++;
        overlayLoadCts?.Cancel();
        prefetchCts?.Cancel();
        inFlightForegroundKey = null;
        inFlightPrefetchKey = null;
        previewTileCache.Clear();
        hasOverlayFailure = false;
        currentOverlayWindow = null;
        RaisePropertyChanged(nameof(CurrentOverlayWindow));
        RaisePropertyChanged(nameof(IsScientificComparisonReady));
        RaisePropertyChanged(nameof(ScientificComparisonLegendText));
        VisualStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPlaybackActive(bool value)
    {
        if (isPlaybackActive == value) return;
        isPlaybackActive = value;
        // Scientific comparison now stays continuously active across Play and Pause using the buffered tile cache;
        // never clear the waveform or flash back to ordinary colored preview.
    }

    public void SetDisplayMontageCompatible(bool value)
    {
        if (IsDisplayMontageCompatible == value) return;
        IsDisplayMontageCompatible = value;
        RaisePropertyChanged(nameof(IsDisplayMontageCompatible));
        ClearOverlayWindow();
        if (IsOverlayCompareEnabled) _ = RefreshOverlayWindowAsync();
    }

    private bool CheckOverlayPreconditions(out IPreprocessingClient? client)
    {
        client = null;
        if (!IsOverlayCompareEnabled || lifetime.IsCancellationRequested) return false;
        if (ScientificInputBlockReason() is { } reason)
        {
            OverlayStatusText = reason;
            return false;
        }
        if (!IsDisplayMontageCompatible)
        {
            OverlayStatusText = "在线临时导联仅用于普通彩色阅图，科学对比已暂停；返回分析导联后自动恢复。";
            return false;
        }
        if (previewPointBudget <= 0)
        {
            OverlayStatusText = "等待绘图区尺寸就绪…";
            return false;
        }
        client = resolveClient();
        if (client is null)
        {
            OverlayStatusText = "当前会话未连接后端，科学对比不可用。";
            return false;
        }
        return true;
    }

    public async Task RefreshOverlayWindowAsync()
    {
        ClearOverlayWindow();
        if (!CheckOverlayPreconditions(out var client) || client is null) return;

        overlayLoadCts?.Dispose();
        overlayLoadCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = overlayLoadCts.Token;
        var generation = overlayGeneration;
        var revision = configurationRevision;
        var budget = previewPointBudget;
        var viewport = getViewportContext();
        var payload = BuildPayload(allowArtifactRemoval: true);

        bool IsCurrent() => !token.IsCancellationRequested && generation == overlayGeneration &&
            revision == configurationRevision && budget == previewPointBudget &&
            IsOverlayCompareEnabled && IsDisplayMontageCompatible &&
            getViewportContext().StartSeconds == viewport.StartSeconds &&
            getViewportContext().WindowSeconds == viewport.WindowSeconds;

        OverlayStatusText = "科学对比加载中（零相位 1–50 Hz，陷波 50 Hz Q=30）…";
        try
        {
            await Task.Delay(80, token);
            var (recId, montageId, _) = await ensureBackendRecordingAsync(token);
            if (!IsCurrent()) return;
            var response = await FetchValidatedPreviewTileAsync(client, recId, montageId,
                Math.Max(0, viewport.StartSeconds), Math.Clamp(viewport.WindowSeconds, 0.05, 60.0), budget, payload, token);
            if (!IsCurrent()) return;
            previewTileCache.Put(revision, Math.Clamp(viewport.WindowSeconds, 0.05, 60.0), response);
            PublishOverlayResponse(response);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            PublishOverlayFailure(ex);
        }
    }

    /// <summary>Viewport-panning & playback entry point: reuses cached aligned tiles with zero flicker and prefetches neighbors.</summary>
    public async Task HandleViewportChangedAsync(double recordingDurationSeconds)
    {
        if (!CheckOverlayPreconditions(out var client) || client is null) return;

        var viewport = getViewportContext();
        var window = Math.Clamp(viewport.WindowSeconds, 0.05, 60.0);
        var start = Math.Max(0, viewport.StartSeconds);
        var revision = configurationRevision;
        var budget = previewPointBudget;

        if (!hasOverlayFailure && previewTileCache.TryComposeForViewport(revision, start, window, recordingDurationSeconds,
                out var cached, out var fullyCovered) && cached is not null)
        {
            if (!ReferenceEquals(currentOverlayWindow, cached))
                PublishOverlayResponse(cached);
            if (fullyCovered)
            {
                _ = PrefetchAdjacentTilesAsync(recordingDurationSeconds);
                return;
            }
        }
        else
        {
            // Keep SuppressesOrdinaryColoredTraces=true while fetching new tiles (no flash back to colored traces),
            // but clear stale out-of-range scientific traces so wrong-time data is never displayed.
            hasOverlayFailure = false;
            if (currentOverlayWindow is not null)
            {
                currentOverlayWindow = null;
                RaisePropertyChanged(nameof(CurrentOverlayWindow));
                RaisePropertyChanged(nameof(IsScientificComparisonReady));
                VisualStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        var missingInitial = previewTileCache.PlanAlignedTiles(revision, budget, start, window, recordingDurationSeconds, neighborMarginTiles: 0);
        var fgKey = $"{revision}|{budget}|{window:0.###}|{string.Join(",", missingInitial.Select(m => $"{m.StartSeconds:0.###}:{m.WindowSeconds:0.###}"))}";
        if (string.Equals(inFlightForegroundKey, fgKey, StringComparison.Ordinal) &&
            overlayLoadCts is { IsCancellationRequested: false })
            return;

        overlayGeneration++;
        overlayLoadCts?.Cancel();
        overlayLoadCts?.Dispose();
        overlayLoadCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        inFlightForegroundKey = fgKey;
        var token = overlayLoadCts.Token;
        var generation = overlayGeneration;
        var payload = BuildPayload(allowArtifactRemoval: true);

        bool IsScopeCurrent() => !token.IsCancellationRequested && generation == overlayGeneration &&
            revision == configurationRevision && budget == previewPointBudget &&
            IsOverlayCompareEnabled && IsDisplayMontageCompatible &&
            Math.Abs(Math.Clamp(getViewportContext().WindowSeconds, 0.05, 60.0) - window) <= 1e-6;

        OverlayStatusText = "科学对比加载中（零相位 1–50 Hz，陷波 50 Hz Q=30）…";
        try
        {
            if (!isPlaybackActive)
                await Task.Delay(35, token);
            var (recId, montageId, duration) = await ensureBackendRecordingAsync(token);
            if (!IsScopeCurrent()) return;
            var effectiveDuration = duration > 0 ? duration : recordingDurationSeconds;
            var missing = previewTileCache.PlanAlignedTiles(revision, budget, start, window, effectiveDuration, neighborMarginTiles: 0);
            foreach (var (tileStart, tileWindow) in missing)
            {
                var tile = await FetchValidatedPreviewTileAsync(client, recId, montageId, tileStart, tileWindow, budget, payload, token);
                if (!IsScopeCurrent()) return;
                previewTileCache.Put(revision, window, tile);
            }
            var latestStart = Math.Max(0, getViewportContext().StartSeconds);
            if (previewTileCache.TryComposeForViewport(revision, latestStart, window, effectiveDuration, out var composed, out _) && composed is not null)
                PublishOverlayResponse(composed);
            _ = PrefetchAdjacentTilesAsync(effectiveDuration);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsScopeCurrent()) return;
            PublishOverlayFailure(ex);
        }
        finally
        {
            if (generation == overlayGeneration)
                inFlightForegroundKey = null;
        }
    }

    public async Task PrefetchAdjacentTilesAsync(double recordingDurationSeconds)
    {
        if (!CheckOverlayPreconditions(out var client) || client is null || hasOverlayFailure) return;
        var viewport = getViewportContext();
        var window = Math.Clamp(viewport.WindowSeconds, 0.05, 60.0);
        var start = Math.Max(0, viewport.StartSeconds);
        var revision = configurationRevision;
        var budget = previewPointBudget;
        var missing = previewTileCache.PlanAlignedTiles(revision, budget, start, window, recordingDurationSeconds, neighborMarginTiles: 1);
        if (missing.Count == 0) return;

        var prefetchKey = $"{revision}|{budget}|{window:0.###}|{string.Join(",", missing.Select(m => $"{m.StartSeconds:0.###}:{m.WindowSeconds:0.###}"))}";
        if (string.Equals(inFlightPrefetchKey, prefetchKey, StringComparison.Ordinal) &&
            prefetchCts is { IsCancellationRequested: false })
            return;

        prefetchCts?.Cancel();
        prefetchCts?.Dispose();
        prefetchCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        inFlightPrefetchKey = prefetchKey;
        var token = prefetchCts.Token;
        var payload = BuildPayload(allowArtifactRemoval: true);
        try
        {
            var (recId, montageId, duration) = await ensureBackendRecordingAsync(token);
            var effectiveDuration = duration > 0 ? duration : recordingDurationSeconds;
            foreach (var (tileStart, tileWindow) in previewTileCache.PlanAlignedTiles(revision, budget, start, window, effectiveDuration, neighborMarginTiles: 1))
            {
                token.ThrowIfCancellationRequested();
                if (revision != configurationRevision || budget != previewPointBudget || !IsOverlayCompareEnabled ||
                    !IsDisplayMontageCompatible || hasOverlayFailure)
                    return;
                var tile = await FetchValidatedPreviewTileAsync(client, recId, montageId, tileStart, tileWindow, budget, payload, token);
                if (revision != configurationRevision || budget != previewPointBudget || !IsOverlayCompareEnabled ||
                    !IsDisplayMontageCompatible || hasOverlayFailure)
                    return;
                previewTileCache.Put(revision, window, tile);
            }
            // Refresh composed trace silently with the newly prefetched contiguous neighbors so subsequent playback/pans are instant.
            var latest = getViewportContext();
            if (revision == configurationRevision && Math.Abs(Math.Clamp(latest.WindowSeconds, 0.05, 60.0) - window) <= 1e-6 &&
                previewTileCache.TryComposeForViewport(revision, Math.Max(0, latest.StartSeconds), window, effectiveDuration, out var expanded, out _) &&
                expanded is not null && !ReferenceEquals(expanded, currentOverlayWindow))
                PublishOverlayResponse(expanded);
        }
        catch { /* Prefetch is strictly opportunistic; foreground requests report errors. */ }
        finally
        {
            if (string.Equals(inFlightPrefetchKey, prefetchKey, StringComparison.Ordinal))
                inFlightPrefetchKey = null;
        }
    }

    private async Task<PreprocessingPreviewWindowResponse> FetchValidatedPreviewTileAsync(
        IPreprocessingClient client, string recId, string? montageId,
        double startSeconds, double windowSeconds, int budget,
        OfflinePreprocessingConfigPayload payload, CancellationToken token)
    {
        var request = new PreprocessingPreviewWindowRequest(
            StartSeconds: Math.Max(0, startSeconds),
            WindowSeconds: windowSeconds,
            MontageId: montageId,
            LowHz: ScientificPreviewLowHz,
            HighHz: ScientificPreviewHighHz,
            NotchHz: ScientificPreviewNotchHz,
            MaxPoints: budget,
            Preprocessing: payload,
            NotchQ: ScientificPreviewNotchQ);
        var response = await client.PreviewPreprocessedWindowAsync(recId, request, token);
        if (response.RecordingId != recId || response.MontageId != montageId)
            throw new InvalidOperationException("科学对比返回的记录或导联与请求不一致。");
        response.Validate();
        response.CommonFilter.ValidateExpected(
            ScientificPreviewLowHz, ScientificPreviewHighHz, ScientificPreviewNotchHz, ScientificPreviewNotchQ);
        if (response.DisplayCompression.TargetPointsPerChannel != budget)
            throw new InvalidOperationException("科学对比返回的显示点数预算与当前绘图区不一致。");
        if (getDisplayedChannels is not null && !MatchesDisplayedChannels(response, getDisplayedChannels()))
            throw new InvalidOperationException("科学对比返回的通道与当前显示通道不匹配。");
        var sampleTolerance = 1.0 / response.SamplingRateHz + 0.000001;
        if (Math.Abs(response.StartSeconds - request.StartSeconds) > sampleTolerance ||
            response.EndSeconds > request.StartSeconds + request.WindowSeconds + sampleTolerance)
            throw new InvalidOperationException("科学对比返回的绝对时间窗与请求不一致。");
        return response;
    }

    private void PublishOverlayResponse(PreprocessingPreviewWindowResponse response)
    {
        hasOverlayFailure = false;
        currentOverlayWindow = response;
        var icaPrefix = response.Processed.IcaModelReused ? "复用已审查 ICA 模型 · " : "";
        var validityNote = ReviewPreprocessingProjection.FormatArtifactRemovalValidityNote(
            response.Processed.ArtifactRemovalValidity);
        OverlayStatusText = (response.EnhancedPreprocessingActive
            ? $"{icaPrefix}科学对比就绪（零相位 1–50 Hz / 50 Hz 陷波 Q=30）：浅灰为科学滤波基线，深灰为科学处理后。"
            : "科学对比就绪（零相位 1–50 Hz / 50 Hz 陷波 Q=30）：增强处理全关，前后曲线完全一致。") + validityNote;
        RaisePropertyChanged(nameof(CurrentOverlayWindow));
        RaisePropertyChanged(nameof(IsScientificComparisonReady));
        RaisePropertyChanged(nameof(ScientificComparisonLegendText));
        VisualStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PublishOverlayFailure(Exception ex)
    {
        hasOverlayFailure = true;
        previewTileCache.Clear();
        currentOverlayWindow = null;
        OverlayStatusText = $"科学对比不可用（已恢复普通彩色阅图）：{ex.Message}";
        RaisePropertyChanged(nameof(CurrentOverlayWindow));
        RaisePropertyChanged(nameof(IsScientificComparisonReady));
        RaisePropertyChanged(nameof(ScientificComparisonLegendText));
        VisualStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
