using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Preprocessing;

/// <summary>Owns cancellable viewport previews; stale results never become displayed evidence.</summary>
public sealed partial class ReviewPreprocessingViewModel
{
    private long overlayGeneration;
    private bool isPlaybackActive;
    public bool IsDisplayMontageCompatible { get; private set; } = true;

    private void ClearOverlayWindow()
    {
        overlayGeneration++;
        overlayLoadCts?.Cancel();
        currentOverlayWindow = null;
        RaisePropertyChanged(nameof(CurrentOverlayWindow));
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
        if (!IsDisplayMontageCompatible || isPlaybackActive)
        {
            OverlayStatusText = !IsDisplayMontageCompatible
                ? "在线临时导联仅用于显示，科学叠图已暂停；返回分析导联后恢复。"
                : "播放中暂停静态科学预览，暂停后重新加载。";
            return;
        }
        var client = resolveClient();
        if (client is null)
        {
            OverlayStatusText = "当前会话未连接后端，无法加载叠图对比。";
            return;
        }
        overlayLoadCts?.Dispose();
        overlayLoadCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = overlayLoadCts.Token;
        var generation = overlayGeneration;
        var revision = configurationRevision;
        var viewport = getViewportContext();
        var payload = BuildPayload(allowArtifactRemoval: true);
        bool IsCurrent() => !token.IsCancellationRequested && generation == overlayGeneration &&
            revision == configurationRevision && IsOverlayCompareEnabled && !isPlaybackActive && IsDisplayMontageCompatible &&
            getViewportContext().StartSeconds == viewport.StartSeconds &&
            getViewportContext().WindowSeconds == viewport.WindowSeconds;
        OverlayStatusText = "正在加载科学预览（1–50 Hz + 50 Hz 陷波）…";
        try
        {
            await Task.Delay(80, token);
            var (recId, montageId, _) = await ensureBackendRecordingAsync(token);
            if (!IsCurrent()) return;
            var request = new PreprocessingPreviewWindowRequest(
                StartSeconds: Math.Max(0, viewport.StartSeconds),
                WindowSeconds: Math.Clamp(viewport.WindowSeconds, 1, 60),
                MontageId: montageId, LowHz: 1.0, HighHz: 50.0, NotchHz: 50.0, MaxPoints: 1000,
                Preprocessing: payload);
            var response = await client.PreviewPreprocessedWindowAsync(recId, request, token);
            if (!IsCurrent()) return;
            if (response.RecordingId != recId || response.MontageId != montageId)
                throw new InvalidOperationException("预览返回的记录或导联与请求不一致。");
            response.Validate();
            var sampleTolerance = 1.0 / response.SamplingRateHz + 0.000001;
            if (Math.Abs(response.StartSeconds - request.StartSeconds) > sampleTolerance ||
                response.EndSeconds > request.StartSeconds + request.WindowSeconds + sampleTolerance)
                throw new InvalidOperationException("预览返回的绝对时间窗与请求不一致。");
            currentOverlayWindow = response;
            OverlayStatusText = (response.IcaModelReused ? "复用已审查 ICA 模型 · " : "") +
                "科学预览 1–50 Hz + 50 Hz 陷波（极值包络显示）；浅灰为本地显示波形，深色为科学处理，青虚线为插值。";
            RaisePropertyChanged(nameof(CurrentOverlayWindow));
            VisualStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            currentOverlayWindow = null;
            OverlayStatusText = $"叠图加载失败：{ex.Message}";
            RaisePropertyChanged(nameof(CurrentOverlayWindow));
            VisualStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
