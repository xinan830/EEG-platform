using System.Collections.ObjectModel;
using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Preprocessing;

/// <summary>Owns offline scientific preprocessing inspection, confirmed configuration, and overlay preview for Review.</summary>
public sealed class ReviewPreprocessingViewModel : ObservableObject, IDisposable
{
    private readonly Func<CancellationToken, Task<(string RecordingId, string? MontageId, double DurationSeconds)>> ensureBackendRecordingAsync;
    private readonly Func<IPreprocessingClient?> resolveClient;
    private readonly Action<double> seekToSeconds;
    private readonly Func<(double StartSeconds, double WindowSeconds, double LowHz, double HighHz, double? NotchHz)> getViewportContext;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? overlayLoadCts;

    private bool isBusy;
    private string statusMessage = "点击「坏导体检」「扫描坏段」或「ICA 审查」开始离线科学预处理。";
    private string badChannelPolicy = "none";
    private string artifactPolicy = "none";
    private double peakToPeakUv = 300;
    private double muscleZScoreThreshold = 4.0;
    private bool showArtifactBandsOnWaveform = true;
    private string cleanRatioSummary = "尚未扫描坏段";
    private string removalMethod = "none";
    private bool allowFrontalEogProxy = true;
    private int icaComponentCount = 15;
    private string? icaFitFingerprint;
    private string? icaEogChannelSource;
    private IReadOnlyList<int>? confirmedIcaExcluded;
    private string icaSummaryText = "尚未进行 ICA 成分审查";
    private bool isOverlayCompareEnabled;
    private PreprocessingPreviewWindowResponse? currentOverlayWindow;
    private string overlayStatusText = "关闭（当前显示本地回放波形）";
    private IReadOnlyList<ArtifactTimelineMarker> artifactMarkers = [];

    public ReviewPreprocessingViewModel(
        Func<CancellationToken, Task<(string RecordingId, string? MontageId, double DurationSeconds)>> ensureBackendRecordingAsync,
        Func<IPreprocessingClient?> resolveClient,
        Action<double> seekToSeconds,
        Func<(double StartSeconds, double WindowSeconds, double LowHz, double HighHz, double? NotchHz)> getViewportContext)
    {
        this.ensureBackendRecordingAsync = ensureBackendRecordingAsync;
        this.resolveClient = resolveClient;
        this.seekToSeconds = seekToSeconds;
        this.getViewportContext = getViewportContext;

        InspectBadChannelsCommand = new AsyncRelayCommand(InspectBadChannelsAsync, ReportError);
        InspectArtifactSegmentsCommand = new AsyncRelayCommand(InspectArtifactSegmentsAsync, ReportError);
        JumpToSegmentCommand = new RelayCommand(param =>
        {
            if (param is ArtifactSegmentRowViewModel seg)
                seekToSeconds(Math.Max(0, seg.StartSeconds - 0.5));
        });
        ResetAllCommand = new RelayCommand(_ => ResetAll());
    }

    public event EventHandler? VisualStateChanged;
    public event EventHandler? ConfigurationChanged;

    public ObservableCollection<BadChannelRowViewModel> BadChannelRows { get; } = [];
    public ObservableCollection<ArtifactSegmentRowViewModel> ArtifactSegments { get; } = [];
    public ObservableCollection<IcaComponentCardViewModel> IcaComponents { get; } = [];

    public ICommand InspectBadChannelsCommand { get; }
    public ICommand InspectArtifactSegmentsCommand { get; }
    public ICommand JumpToSegmentCommand { get; }
    public ICommand ResetAllCommand { get; }

    public bool IsBusy { get => isBusy; private set => SetProperty(ref isBusy, value); }
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public string CleanRatioSummary { get => cleanRatioSummary; private set => SetProperty(ref cleanRatioSummary, value); }
    public string IcaSummaryText { get => icaSummaryText; private set => SetProperty(ref icaSummaryText, value); }
    public string OverlayStatusText { get => overlayStatusText; private set => SetProperty(ref overlayStatusText, value); }
    public string? IcaFitFingerprint => icaFitFingerprint;
    public IReadOnlyList<int>? ConfirmedIcaExcluded => confirmedIcaExcluded;
    public PreprocessingPreviewWindowResponse? CurrentOverlayWindow => currentOverlayWindow;

    public IReadOnlyList<ArtifactTimelineMarker> ArtifactMarkers
    {
        get => showArtifactBandsOnWaveform ? artifactMarkers : [];
        private set
        {
            artifactMarkers = value;
            RaisePropertyChanged(nameof(ArtifactMarkers));
            VisualStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string BadChannelPolicy
    {
        get => badChannelPolicy;
        set
        {
            if (!SetProperty(ref badChannelPolicy, value)) return;
            InvalidateIcaFingerprint("坏导策略已变更，需重新审查 ICA 成分");
            NotifyConfigAndVisuals();
        }
    }

    public string ArtifactPolicy
    {
        get => artifactPolicy;
        set
        {
            if (!SetProperty(ref artifactPolicy, value)) return;
            InvalidateIcaFingerprint("坏段策略已变更，需重新审查 ICA 成分");
            NotifyConfigAndVisuals();
        }
    }

    public double PeakToPeakUv
    {
        get => peakToPeakUv;
        set
        {
            if (!SetProperty(ref peakToPeakUv, Math.Clamp(value, 50, 2000))) return;
            InvalidateIcaFingerprint("爆幅阈值已变更，需重新审查 ICA 成分");
            NotifyConfigAndVisuals();
        }
    }

    public double MuscleZScoreThreshold
    {
        get => muscleZScoreThreshold;
        set
        {
            if (!SetProperty(ref muscleZScoreThreshold, Math.Clamp(value, 1.5, 12.0))) return;
            InvalidateIcaFingerprint("肌电阈值已变更，需重新审查 ICA 成分");
            NotifyConfigAndVisuals();
        }
    }

    public bool ShowArtifactBandsOnWaveform
    {
        get => showArtifactBandsOnWaveform;
        set
        {
            if (!SetProperty(ref showArtifactBandsOnWaveform, value)) return;
            RaisePropertyChanged(nameof(ArtifactMarkers));
            VisualStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string RemovalMethod
    {
        get => removalMethod;
        set
        {
            if (!SetProperty(ref removalMethod, value)) return;
            NotifyConfigAndVisuals();
        }
    }

    public bool AllowFrontalEogProxy
    {
        get => allowFrontalEogProxy;
        set
        {
            if (!SetProperty(ref allowFrontalEogProxy, value)) return;
            InvalidateIcaFingerprint("眼电代理设置已变更，需重新审查 ICA 成分");
            NotifyConfigAndVisuals();
        }
    }

    public int IcaComponentCount
    {
        get => icaComponentCount;
        set
        {
            if (!SetProperty(ref icaComponentCount, Math.Clamp(value, 2, 32))) return;
            InvalidateIcaFingerprint("ICA 成分数已变更，需重新审查 ICA 成分");
            NotifyConfigAndVisuals();
        }
    }

    public bool IsOverlayCompareEnabled
    {
        get => isOverlayCompareEnabled;
        set
        {
            if (!SetProperty(ref isOverlayCompareEnabled, value)) return;
            if (!value)
            {
                overlayLoadCts?.Cancel();
                currentOverlayWindow = null;
                OverlayStatusText = "关闭（当前显示本地回放波形）";
                RaisePropertyChanged(nameof(CurrentOverlayWindow));
                VisualStateChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                OverlayStatusText = "正在加载清洗后波形对比…";
                _ = RefreshOverlayWindowAsync();
            }
        }
    }

    public bool HasAnyActivePreprocessing =>
        (BadChannelPolicy != "none" && SelectedBadChannels.Count > 0) ||
        ArtifactPolicy != "none" ||
        (RemovalMethod != "none" && (RemovalMethod != "ica_infomax" || !string.IsNullOrWhiteSpace(icaFitFingerprint)));

    public IReadOnlyList<string> SelectedBadChannels =>
        BadChannelRows.Where(row => row.IsSelected).Select(row => row.Channel).ToArray();

    public string ActiveSummaryBadge => ReviewPreprocessingProjection.FormatActiveSummaryBadge(
        BadChannelPolicy, SelectedBadChannels.Count, ArtifactPolicy, RemovalMethod, icaFitFingerprint, confirmedIcaExcluded);

    public ChannelVisualBadge ResolveChannelBadge(string displayedChannelName) =>
        ReviewPreprocessingProjection.ResolveChannelBadge(displayedChannelName, BadChannelRows, BadChannelPolicy);

    public async Task InspectBadChannelsAsync()
    {
        var client = RequireClient();
        IsBusy = true;
        StatusMessage = "正在扫描原始未投影 EEG 通道的坏导指标…";
        try
        {
            var (recId, _, duration) = await ensureBackendRecordingAsync(lifetime.Token);
            var response = await client.InspectBadChannelsAsync(recId, 0, Math.Min(Math.Max(duration, 4), 180), lifetime.Token);
            BadChannelRows.Clear();
            foreach (var item in response.Channels)
                BadChannelRows.Add(new BadChannelRowViewModel(item, OnBadChannelSelectionChanged));

            if (response.RecommendedBadChannels.Count > 0 && BadChannelPolicy == "none")
            {
                var allCanInterp = response.Channels.Where(c => c.IsBad).All(c => c.CanInterpolate);
                badChannelPolicy = allCanInterp ? "interpolate" : "exclude";
                RaisePropertyChanged(nameof(BadChannelPolicy));
            }
            StatusMessage = response.RecommendedBadChannels.Count == 0
                ? $"坏导体检完成（{response.Channels.Count} 导）：未发现坏导。"
                : $"坏导体检完成：建议处理 {response.RecommendedBadChannels.Count} 路坏导（{string.Join(", ", response.RecommendedBadChannels)}）。";
            NotifyConfigAndVisuals();
        }
        finally { IsBusy = false; }
    }

    public async Task InspectArtifactSegmentsAsync()
    {
        var client = RequireClient();
        IsBusy = true;
        StatusMessage = "正在扫描爆幅峰峰值与 MNE 宽带肌电坏段…";
        try
        {
            var (recId, montageId, duration) = await ensureBackendRecordingAsync(lifetime.Token);
            var response = await client.InspectArtifactSegmentsAsync(
                recId, 0, Math.Min(Math.Max(duration, 4), 180), montageId,
                PeakToPeakUv, MuscleZScoreThreshold, SelectedBadChannels, BadChannelPolicy, lifetime.Token);
            ArtifactSegments.Clear();
            foreach (var seg in response.Segments)
                ArtifactSegments.Add(new ArtifactSegmentRowViewModel(seg));

            ArtifactMarkers = ArtifactSegments.Select(s => s.ToMarker()).ToArray();
            CleanRatioSummary = $"干净率 {response.CleanRatio * 100:0.1}% · 检出 {response.Segments.Count} 段坏段（{ReviewPreprocessingProjection.FormatMuscleStatus(response.MuscleCheckStatus)}）";
            if (response.Segments.Count > 0 && ArtifactPolicy == "none")
            {
                artifactPolicy = "reject";
                RaisePropertyChanged(nameof(ArtifactPolicy));
            }
            StatusMessage = response.Segments.Count == 0
                ? "坏段扫描完成：整段信号干净，无超限坏段。"
                : $"坏段扫描完成：检出 {response.Segments.Count} 段，点击列表可一键跳转定位。";
            NotifyConfigAndVisuals();
        }
        finally { IsBusy = false; }
    }

    public async Task<IcaInspectionResponse> InspectIcaForDialogAsync()
    {
        var client = RequireClient();
        IsBusy = true;
        StatusMessage = "正在整段连续拟合 Infomax ICA 并生成各成分 2D 头皮热力图与波形（约 5~20 秒）…";
        try
        {
            var (recId, montageId, _) = await ensureBackendRecordingAsync(lifetime.Token);
            var response = await client.InspectIcaComponentsAsync(
                recId, montageId, lowHz: 1.0, highHz: 50.0, notchHz: 50.0,
                badChannels: SelectedBadChannels, badChannelPolicy: BadChannelPolicy,
                artifactPolicy: ArtifactPolicy, peakToPeakUv: PeakToPeakUv,
                muscleZScoreThreshold: MuscleZScoreThreshold,
                allowFrontalEogProxy: AllowFrontalEogProxy,
                icaComponentCount: IcaComponentCount, icaEogThreshold: 0.7,
                cancellationToken: lifetime.Token);

            IcaComponents.Clear();
            var preselected = (confirmedIcaExcluded is not null && string.Equals(icaFitFingerprint, response.IcaFitFingerprint, StringComparison.Ordinal))
                ? confirmedIcaExcluded.ToHashSet()
                : response.RecommendedExcludeComponents.ToHashSet();
            foreach (var comp in response.Components)
                IcaComponents.Add(new IcaComponentCardViewModel(comp, preselected.Contains(comp.ComponentIndex)));

            icaFitFingerprint = response.IcaFitFingerprint;
            icaEogChannelSource = response.EogChannelSource;
            RaisePropertyChanged(nameof(IcaFitFingerprint));
            StatusMessage = $"ICA 拟合完成（共 {response.ComponentCount} 个成分，建议剔除：{ReviewPreprocessingProjection.FormatComponentList(response.RecommendedExcludeComponents)}）。";
            return response;
        }
        finally { IsBusy = false; }
    }

    public void ConfirmIcaSelection(IReadOnlyList<int> excludedIndices, string? fitFingerprint)
    {
        icaFitFingerprint = fitFingerprint;
        confirmedIcaExcluded = excludedIndices.Distinct().OrderBy(i => i).ToArray();
        removalMethod = "ica_infomax";
        RaisePropertyChanged(nameof(RemovalMethod));
        RaisePropertyChanged(nameof(IcaFitFingerprint));
        RaisePropertyChanged(nameof(ConfirmedIcaExcluded));
        var proxyNote = icaEogChannelSource == "frontal_eeg_proxy" ? "（前额代理）" : "";
        IcaSummaryText = $"已确认剔除 {ReviewPreprocessingProjection.FormatComponentList(confirmedIcaExcluded)}{proxyNote} · 指纹 {fitFingerprint?[..Math.Min(8, fitFingerprint.Length)]}";
        StatusMessage = $"ICA 成分选择已锁定（{IcaSummaryText}），后续 PSD/RBP 等算法将直接复用模型。";
        NotifyConfigAndVisuals();
    }

    public async Task RefreshOverlayWindowAsync()
    {
        if (!IsOverlayCompareEnabled) return;
        var client = resolveClient();
        if (client is null)
        {
            OverlayStatusText = "当前会话未连接后端，无法加载叠图对比。";
            return;
        }
        overlayLoadCts?.Cancel();
        overlayLoadCts?.Dispose();
        overlayLoadCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = overlayLoadCts.Token;
        try
        {
            await Task.Delay(80, token);
            var (recId, montageId, _) = await ensureBackendRecordingAsync(token);
            var (startS, windowS, _, _, _) = getViewportContext();
            var request = new PreprocessingPreviewWindowRequest(
                StartSeconds: Math.Max(0, startS),
                WindowSeconds: Math.Clamp(windowS, 1, 60),
                MontageId: montageId,
                LowHz: 1.0, HighHz: 50.0, NotchHz: 50.0, MaxPoints: 1000,
                Preprocessing: BuildPayload(allowArtifactRemoval: true));
            var response = await client.PreviewPreprocessedWindowAsync(recId, request, token);
            if (token.IsCancellationRequested) return;
            currentOverlayWindow = response;
            OverlayStatusText = response.IcaModelReused
                ? "叠图对比已开启（复用已审查 ICA 模型 · 浅灰=原波形，深色=清洗后，青虚线=插值修复）"
                : "叠图对比已开启（浅灰=原波形，深色=清洗后，青虚线=插值修复）";
            RaisePropertyChanged(nameof(CurrentOverlayWindow));
            VisualStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { OverlayStatusText = $"叠图加载失败：{ex.Message}"; }
    }

    public OfflinePreprocessingConfigPayload BuildPayload(bool allowArtifactRemoval) =>
        ReviewPreprocessingProjection.BuildPayload(
            BadChannelPolicy, SelectedBadChannels, ArtifactPolicy, PeakToPeakUv, MuscleZScoreThreshold,
            RemovalMethod, AllowFrontalEogProxy, IcaComponentCount, confirmedIcaExcluded, icaFitFingerprint,
            allowArtifactRemoval);

    public Dictionary<string, object?>? BuildAlgorithmConfigDictionary(bool allowArtifactRemoval) =>
        ReviewPreprocessingProjection.ToAlgorithmConfigDictionary(BuildPayload(allowArtifactRemoval));

    public void ResetAll()
    {
        badChannelPolicy = "none";
        artifactPolicy = "none";
        removalMethod = "none";
        icaFitFingerprint = null;
        confirmedIcaExcluded = null;
        isOverlayCompareEnabled = false;
        currentOverlayWindow = null;
        ArtifactSegments.Clear();
        ArtifactMarkers = [];
        CleanRatioSummary = "尚未扫描坏段";
        IcaSummaryText = "尚未进行 ICA 成分审查";
        OverlayStatusText = "关闭（当前显示本地回放波形）";
        StatusMessage = "已重置全部离线科学预处理配置。";
        RaisePropertyChanged(nameof(BadChannelPolicy));
        RaisePropertyChanged(nameof(ArtifactPolicy));
        RaisePropertyChanged(nameof(RemovalMethod));
        RaisePropertyChanged(nameof(IsOverlayCompareEnabled));
        RaisePropertyChanged(nameof(CurrentOverlayWindow));
        NotifyConfigAndVisuals();
    }

    public void Dispose()
    {
        overlayLoadCts?.Cancel();
        overlayLoadCts?.Dispose();
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private IPreprocessingClient RequireClient() =>
        resolveClient() ?? throw new InvalidOperationException("当前回放未连接后端预处理服务。");

    private void OnBadChannelSelectionChanged()
    {
        InvalidateIcaFingerprint("坏导勾选已变更，需重新审查 ICA 成分");
        NotifyConfigAndVisuals();
    }

    private void InvalidateIcaFingerprint(string reason)
    {
        if (string.IsNullOrWhiteSpace(icaFitFingerprint)) return;
        icaFitFingerprint = null;
        confirmedIcaExcluded = null;
        IcaSummaryText = reason;
        RaisePropertyChanged(nameof(IcaFitFingerprint));
        RaisePropertyChanged(nameof(ConfirmedIcaExcluded));
    }

    private void NotifyConfigAndVisuals()
    {
        RaisePropertyChanged(nameof(HasAnyActivePreprocessing));
        RaisePropertyChanged(nameof(ActiveSummaryBadge));
        RaisePropertyChanged(nameof(SelectedBadChannels));
        VisualStateChanged?.Invoke(this, EventArgs.Empty);
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        if (IsOverlayCompareEnabled)
            _ = RefreshOverlayWindowAsync();
    }

    private void ReportError(Exception ex)
    {
        IsBusy = false;
        StatusMessage = $"操作失败：{ex.Message}";
    }
}
