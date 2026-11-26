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

    private long overlayGeneration;
    private bool isPlaybackActive;
    private int previewPointBudget = 1200;

    public bool IsDisplayMontageCompatible { get; private set; } = true;
    public int PreviewPointBudget => previewPointBudget;

    public bool IsScientificComparisonReady =>
        !isPlaybackActive && IsDisplayMontageCompatible &&
        IsOverlayCompareEnabled && currentOverlayWindow is { } preview &&
        (getDisplayedChannels is null || MatchesDisplayedChannels(preview, getDisplayedChannels()));

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
        ClearOverlayWindow();
        if (!IsOverlayCompareEnabled) return;
        _ = RefreshOverlayWindowAsync();
    }

    public void SetDisplayMontageCompatible(bool value)
    {
        if (IsDisplayMontageCompatible == value) return;
        IsDisplayMontageCompatible = value;
        RaisePropertyChanged(nameof(IsDisplayMontageCompatible));
        ClearOverlayWindow();
        if (IsOverlayCompareEnabled) _ = RefreshOverlayWindowAsync();
    }

    public async Task RefreshOverlayWindowAsync()
    {
        ClearOverlayWindow();
        if (!IsOverlayCompareEnabled || lifetime.IsCancellationRequested) return;
        if (ScientificInputBlockReason() is { } reason)
        {
            OverlayStatusText = reason;
            return;
        }
        if (!IsDisplayMontageCompatible || isPlaybackActive)
        {
            OverlayStatusText = !IsDisplayMontageCompatible
                ? "在线临时导联仅用于普通彩色阅图，科学对比已暂停；返回分析导联后自动恢复。"
                : "播放中已暂停科学对比，恢复普通彩色阅图；暂停后重新加载。";
            return;
        }
        if (previewPointBudget <= 0)
        {
            OverlayStatusText = "等待绘图区尺寸就绪…";
            return;
        }
        var client = resolveClient();
        if (client is null)
        {
            OverlayStatusText = "当前会话未连接后端，科学对比不可用。";
            return;
        }

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
            IsOverlayCompareEnabled && !isPlaybackActive && IsDisplayMontageCompatible &&
            getViewportContext().StartSeconds == viewport.StartSeconds &&
            getViewportContext().WindowSeconds == viewport.WindowSeconds;

        OverlayStatusText = "科学对比加载中（零相位 1–50 Hz，陷波 50 Hz Q=30）…";
        try
        {
            await Task.Delay(80, token);
            var (recId, montageId, _) = await ensureBackendRecordingAsync(token);
            if (!IsCurrent()) return;
            var requestedWindow = Math.Clamp(viewport.WindowSeconds, 0.05, 60.0);
            var request = new PreprocessingPreviewWindowRequest(
                StartSeconds: Math.Max(0, viewport.StartSeconds),
                WindowSeconds: requestedWindow,
                MontageId: montageId,
                LowHz: ScientificPreviewLowHz,
                HighHz: ScientificPreviewHighHz,
                NotchHz: ScientificPreviewNotchHz,
                MaxPoints: budget,
                Preprocessing: payload,
                NotchQ: ScientificPreviewNotchQ);
            var response = await client.PreviewPreprocessedWindowAsync(recId, request, token);
            if (!IsCurrent()) return;
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
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            currentOverlayWindow = null;
            OverlayStatusText = $"科学对比不可用（已恢复普通彩色阅图）：{ex.Message}";
            RaisePropertyChanged(nameof(CurrentOverlayWindow));
            RaisePropertyChanged(nameof(IsScientificComparisonReady));
            RaisePropertyChanged(nameof(ScientificComparisonLegendText));
            VisualStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
