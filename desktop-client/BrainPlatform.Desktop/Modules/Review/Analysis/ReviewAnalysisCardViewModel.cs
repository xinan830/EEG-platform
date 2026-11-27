using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Shared.Controls.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Per-open-panel state, binding ownership, and scoped bindings for Review panels.</summary>
public sealed class ReviewAnalysisCardViewModel : ObservableObject
{
    private readonly RecordingReviewViewModel owner;
    private readonly ReviewAlgorithmPanelRegistration? registration;
    private double startSeconds;
    private string preparationText = "算法未准备。";
    private bool isStale;
    private bool isFloating;
    private bool isMaximized;
    private bool isFocused;

    internal ReviewAnalysisCardViewModel(
        RecordingReviewViewModel owner,
        string algorithmId,
        string displayName,
        ReviewAnalysisPanelKind panelKind,
        ReviewAlgorithmPanelRegistration? registration,
        ReviewAnalysisCoordinator coordinator,
        double startSeconds)
    {
        this.owner = owner;
        AlgorithmId = algorithmId;
        DisplayName = displayName;
        PanelKind = panelKind;
        this.registration = registration;
        Coordinator = coordinator;
        this.startSeconds = startSeconds;
        CloseAnalysisCommand = new AsyncRelayCommand(() => owner.CloseAnalysisAsync(AlgorithmId), owner.ReportAnalysisCommandError);
        DockCommand = new RelayCommand(() => owner.ToggleFloatingSlot(AlgorithmId));
        MaximizeCommand = new RelayCommand(() => owner.ToggleMaximizeSlot(AlgorithmId));
        FocusCommand = new RelayCommand(() => owner.FocusAnalysisSlot(AlgorithmId));
    }

    public string AlgorithmId { get; }
    public string SourceAlgorithmId => AlgorithmId is "spectral-values" or "custom-frequency-inspection" ? "psd" : AlgorithmId;
    public string DisplayName { get; }
    public ReviewAnalysisPanelKind PanelKind { get; }
    public ReviewAnalysisPanelKind ActiveAnalysisPanelKind => PanelKind;
    public ReviewAlgorithmPanelRegistration? Registration => registration;
    public bool IsBottomPreferred => AlgorithmId == "tnk_biofeedback";
    public bool IsSpectralInspection => AlgorithmId is "spectral-values" or "custom-frequency-inspection";

    public string CardHeaderTitle => PanelKind switch
    {
        ReviewAnalysisPanelKind.Rbp => "RBP",
        ReviewAnalysisPanelKind.SpectralValues => "频段数值",
        ReviewAnalysisPanelKind.CustomFrequencyInspection => "自定义频段",
        ReviewAnalysisPanelKind.Psd => "PSD",
        ReviewAnalysisPanelKind.Stft => "STFT",
        ReviewAnalysisPanelKind.Tnk => "TNK 脑指标",
        ReviewAnalysisPanelKind.Scalar => AlgorithmId switch
        {
            "faa" => "FAA",
            "iapf" => "IAPF",
            "theta_beta" => "Theta/Beta",
            "brainbeat" => "脑节律",
            "brainbeat_individualized" => "个体化 BrainBeat",
            _ => DisplayName,
        },
        _ => DisplayName,
    };

    internal ReviewAnalysisCoordinator Coordinator { get; }
    internal ReviewAnalysisBinding? Binding { get; set; }
    internal ReviewAnalysisBinding? SpectralRbpBinding { get; set; }
    internal ReviewPlaybackBuffer<ReviewPreparedSpectralWindow>? SpectralPlaybackBuffer { get; set; }
    internal ReviewPreparedSpectralWindow? PreparedSpectralWindow { get; set; }
    internal CancellationTokenSource? SpectralWindowCancellation { get; set; }
    internal Task? SpectralWindowLoad { get; set; }
    internal long? PendingSpectralPlaybackSample { get; set; }
    internal long LastSpectralPlaybackSample { get; set; } = -1;
    internal bool SpectralSourcesLoading { get; set; }
    internal CancellationTokenSource? TopomapCancellation { get; set; }
    internal long TopomapRequestRevision;
    internal SemaphoreSlim PreparationGate { get; } = new(1, 1);
    internal CancellationTokenSource? PreparationCancellation { get; set; }
    internal long ConfigurationRevision;
    internal bool IsClosed { get; set; }

    public ICommand CloseAnalysisCommand { get; }
    public ICommand DockCommand { get; }
    public ICommand MaximizeCommand { get; }
    public ICommand FocusCommand { get; }

    public bool CanMaximize => !IsBottomPreferred && !IsFloating;

    public bool IsFloating
    {
        get => isFloating;
        internal set
        {
            if (SetProperty(ref isFloating, value))
            {
                if (value && isMaximized) IsMaximized = false;
                RaisePropertyChanged(nameof(CanMaximize));
                RaisePropertyChanged(nameof(DockToolTip));
            }
        }
    }

    public bool IsMaximized
    {
        get => isMaximized;
        internal set
        {
            if (SetProperty(ref isMaximized, value))
                RaisePropertyChanged(nameof(MaximizeToolTip));
        }
    }

    public bool IsFocused
    {
        get => isFocused;
        internal set => SetProperty(ref isFocused, value);
    }

    public bool IsStale
    {
        get => isStale;
        internal set
        {
            if (SetProperty(ref isStale, value))
            {
                RaiseWindowStateChanged();
            }
        }
    }

    public double AnalysisPanelStartSeconds
    {
        get => startSeconds;
        internal set => SetProperty(ref startSeconds, value);
    }

    public string AnalysisPreparationText
    {
        get => preparationText;
        internal set => SetProperty(ref preparationText, value);
    }

    public string DockToolTip => IsFloating ? "停靠回主工作区" : "弹出为独立窗口";
    public string MaximizeToolTip => IsMaximized ? "还原为自动网格布局" : "放大独占右侧分析区";

    public WindowPreviewResponse? CurrentWindow => IsStale ? null : Binding?.CurrentWindow;
    public long TargetAnalysisCursorSample => Binding?.TargetCursorSample ?? -1;
    public long? DisplayedAnalysisCursorSample => IsStale ? null : Binding?.DisplayedCursorSample;
    public string AnalysisWindowState => IsStale ? "stale" : Binding?.DisplayedWindowStatus ?? "none";
    public bool IsAnalysisTargetDisplayed => !IsStale && Binding?.IsTargetDisplayed == true;
    public bool IsAnalysisLoading => Binding?.IsLoading ?? false;
    public string AnalysisStatusText => IsStale ? "参数已改变，等待自动分析。" : Binding?.StatusText ?? "未绑定分析结果。";
    public bool IsAnalysisAttached => Binding?.RunId is not null || Binding?.IsOnDemandAttached == true;

    // Shared panel view models & owner playback context
    public RbpReviewAnalysisViewModel RbpAnalysis => owner.RbpAnalysis;
    public PsdReviewAnalysisViewModel PsdAnalysis => owner.PsdAnalysis;
    public ReviewStftParameters StftParameters => owner.StftParameters;
    public StftReviewAnalysisViewModel StftAnalysis => owner.StftAnalysis;
    public ReviewTnkParameters TnkParameters => owner.TnkParameters;
    public ReviewTnkAnalysisViewModel TnkAnalysis => owner.TnkAnalysis;
    public ReviewSpectralValuesViewModel SpectralValues => owner.SpectralValues;
    public ReviewCustomFrequencyInspectionViewModel CustomFrequencyInspection => owner.CustomFrequencyInspection;
    public BrainPlatform.Desktop.Shared.Time.RecordingTimeDisplayContext TimeDisplay => owner.TimeDisplay;
    public double PositionSeconds => owner.PositionSeconds;

    // Card-scoped scalar properties so multiple scalar panels stay completely independent
    public ReviewScalarParameters? ActiveScalarParameters => registration?.Configuration as ReviewScalarParameters;
    public ReviewScalarAnalysisViewModel? ActiveScalarAnalysis =>
        PanelKind == ReviewAnalysisPanelKind.Scalar ? registration?.Presentation as ReviewScalarAnalysisViewModel : null;
    public bool HasSecondaryScalarChannel => AlgorithmId is "faa" or "brainbeat" or "brainbeat_individualized";

    internal void ApplyPresentation(WindowPreviewResponse? response)
    {
        if (IsSpectralInspection) return;
        registration?.Apply(response, startSeconds);
    }

    internal void RaisePositionChanged() => RaisePropertyChanged(nameof(PositionSeconds));

    internal void RaiseWindowStateChanged()
    {
        RaisePropertyChanged(nameof(CurrentWindow));
        RaisePropertyChanged(nameof(TargetAnalysisCursorSample));
        RaisePropertyChanged(nameof(DisplayedAnalysisCursorSample));
        RaisePropertyChanged(nameof(AnalysisWindowState));
        RaisePropertyChanged(nameof(IsAnalysisTargetDisplayed));
        RaisePropertyChanged(nameof(IsAnalysisLoading));
        RaisePropertyChanged(nameof(AnalysisStatusText));
        RaisePropertyChanged(nameof(IsAnalysisAttached));
    }
}
