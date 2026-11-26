using System.Collections.ObjectModel;
using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Preprocessing;

/// <summary>Owns offline scientific preprocessing inspection, confirmed configuration, and overlay preview for Review.</summary>
public sealed partial class ReviewPreprocessingViewModel : ObservableObject, IDisposable
{
    private readonly Func<CancellationToken, Task<(string RecordingId, string? MontageId, double DurationSeconds)>> ensureBackendRecordingAsync;
    private readonly Func<IPreprocessingClient?> resolveClient;
    private readonly Func<IReadOnlyList<string>>? getDisplayedChannels;
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
    private IcaInspectionResponse? pendingIca;
    private long pendingIcaRevision;
    private long configurationRevision;
    private IReadOnlyList<int>? confirmedIcaExcluded;
    private string icaSummaryText = "尚未进行 ICA 成分审查";
    private bool isOverlayCompareEnabled;
    private PreprocessingPreviewWindowResponse? currentOverlayWindow;
    private string overlayStatusText = "关闭（当前显示普通彩色阅图）";
    private IReadOnlyList<ArtifactTimelineMarker> artifactMarkers = [];

    public ReviewPreprocessingViewModel(
        Func<CancellationToken, Task<(string RecordingId, string? MontageId, double DurationSeconds)>> ensureBackendRecordingAsync,
        Func<IPreprocessingClient?> resolveClient,
        Action<double> seekToSeconds,
        Func<(double StartSeconds, double WindowSeconds, double LowHz, double HighHz, double? NotchHz)> getViewportContext,
        Func<PreprocessingRecordingContext>? getRecordingContext = null,
        Func<IReadOnlyList<string>>? getDisplayedChannels = null)
    {
        this.ensureBackendRecordingAsync = ensureBackendRecordingAsync;
        this.resolveClient = resolveClient;
        this.seekToSeconds = seekToSeconds;
        this.getViewportContext = getViewportContext;
        this.getRecordingContext = getRecordingContext;
        this.getDisplayedChannels = getDisplayedChannels;

        InspectBadChannelsCommand = new AsyncRelayCommand(InspectBadChannelsAsync, ReportError);
        InspectArtifactSegmentsCommand = new AsyncRelayCommand(InspectArtifactSegmentsAsync, ReportError);
        JumpToSegmentCommand = new SegmentJumpCommand(seekToSeconds);
        ResetAllCommand = new RelayCommand(ResetAll);
        SaveChannelIdentitiesCommand = new AsyncRelayCommand(SaveChannelIdentitiesAsync, ReportError);
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
    public string ArtifactRemovalScopeNotice => "STFT 时频图与 TNK 有界分析仅应用坏导/坏段策略，不应用 ICA、SSP 或 EOG 去伪迹，与清洗预览的输入不同。";
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
            InvalidateArtifactInspection();
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
            InvalidateArtifactInspection();
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
            InvalidateArtifactInspection();
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
            InvalidateArtifactInspection();
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
                ClearOverlayWindow();
                OverlayStatusText = "关闭（当前显示普通彩色阅图）";
            }
            else
            {
                OverlayStatusText = "科学对比加载中（零相位 1–50 Hz，陷波 50 Hz Q=30）…";
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
        ReviewPreprocessingProjection.ResolveChannelBadge(displayedChannelName, BadChannelRows, BadChannelPolicy, CurrentOverlayWindow);

    public OfflinePreprocessingConfigPayload BuildPayload(bool allowArtifactRemoval)
    {
        ValidateReferenceSelection();
        return ReviewPreprocessingProjection.BuildPayload(
            BadChannelPolicy, SelectedBadChannels, ArtifactPolicy, PeakToPeakUv, MuscleZScoreThreshold,
            RemovalMethod, AllowFrontalEogProxy, IcaComponentCount, confirmedIcaExcluded, icaFitFingerprint,
            allowArtifactRemoval, SelectedEogChannels, SelectedEcgChannels);
    }

    public Dictionary<string, object?>? BuildAlgorithmConfigDictionary(bool allowArtifactRemoval) =>
        ReviewPreprocessingProjection.ToAlgorithmConfigDictionary(BuildPayload(allowArtifactRemoval));

    public void ResetAll()
    {
        ResetReferenceChannels();
        InvalidateIcaFingerprint("尚未进行 ICA 成分审查", markPendingReview: false);
        BadChannelRows.Clear();
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
        OverlayStatusText = "关闭（当前显示普通彩色阅图）";
        StatusMessage = "已重置全部离线科学预处理配置。";
        RaisePropertyChanged(nameof(BadChannelPolicy));
        RaisePropertyChanged(nameof(ArtifactPolicy));
        RaisePropertyChanged(nameof(RemovalMethod));
        RaisePropertyChanged(nameof(IsOverlayCompareEnabled));
        RaisePropertyChanged(nameof(CurrentOverlayWindow));
        RaisePropertyChanged(nameof(IcaFitFingerprint));
        RaisePropertyChanged(nameof(ConfirmedIcaExcluded));
        NotifyConfigAndVisuals();
    }

    public void Dispose()
    {
        CancelInspection();
        ClearOverlayWindow();
        overlayLoadCts?.Dispose();
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private IPreprocessingClient RequireClient() =>
        resolveClient() ?? throw new InvalidOperationException("当前回放未连接后端预处理服务。");

    public void InvalidateSignalInputs()
    {
        InvalidateIcaFingerprint("导联或输入信号已变更，需重新审查 ICA 成分");
        ArtifactSegments.Clear();
        ArtifactMarkers = [];
        CleanRatioSummary = "输入已变更，请重新扫描坏段";
        NotifyConfigAndVisuals();
    }

    private void OnBadChannelSelectionChanged()
    {
        InvalidateArtifactInspection();
        InvalidateIcaFingerprint("坏导勾选已变更，需重新审查 ICA 成分");
        NotifyConfigAndVisuals();
    }

    private void InvalidateIcaFingerprint(string reason, bool markPendingReview = true)
    {
        if (icaFitFingerprint is not null && RemovalMethod == "ica_infomax" &&
            (SelectedEogChannels.Count > 0 || SelectedEcgChannels.Count > 0)) InvalidateArtifactInspection();
        var hadReview = pendingIca is not null || icaFitFingerprint is not null || IcaComponents.Count > 0;
        CancelInspection();
        pendingIca = null;
        IcaComponents.Clear();
        icaFitFingerprint = null;
        confirmedIcaExcluded = null;
        icaInvalidatedPendingReview = markPendingReview && hadReview;
        if (hadReview) IcaSummaryText = reason;
        RaisePropertyChanged(nameof(IcaFitFingerprint));
        RaisePropertyChanged(nameof(ConfirmedIcaExcluded));
    }

    private void NotifyConfigAndVisuals()
    {
        configurationRevision++;
        CancelInspection();
        ClearOverlayWindow();
        UpdateReferenceValidationState();
        RaisePropertyChanged(nameof(HasAnyActivePreprocessing));
        RaisePropertyChanged(nameof(ActiveSummaryBadge));
        RaisePropertyChanged(nameof(SelectedBadChannels));
        RaisePipelineNodeProperties();
        VisualStateChanged?.Invoke(this, EventArgs.Empty);
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        if (IsOverlayCompareEnabled && !AreScientificInputsBlocked)
            _ = RefreshOverlayWindowAsync();
    }

    private void ReportError(Exception ex)
    {
        if (ex is OperationCanceledException) return;
        IsBusy = false;
        StatusMessage = $"操作失败：{ex.Message}";
    }

    private sealed class SegmentJumpCommand(Action<double> seek) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => parameter is ArtifactSegmentRowViewModel;
        public void Execute(object? parameter)
        {
            if (parameter is ArtifactSegmentRowViewModel seg)
                seek(Math.Max(0, seg.StartSeconds - 0.5));
        }
    }
}
