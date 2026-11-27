using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public enum LiveAnalysisPanelKind
{
    Rbp,
    Psd,
    Stft,
}

/// <summary>Per-open-panel state, commands, and dock chrome metadata for live Acquisition analysis cards.</summary>
public sealed class LiveAnalysisCardViewModel : ObservableObject
{
    private readonly Action<LiveAnalysisCardViewModel>? onChannelChanged;
    private IReadOnlyList<string> channels = [];
    private string? selectedChannel;
    private string statusText = "等待可用的 10 秒窗口";
    private bool isFloating;
    private bool isMaximized;
    private bool isFocused;

    internal LiveAnalysisCardViewModel(
        LiveAnalysisWorkspaceViewModel owner,
        string algorithmId,
        string displayName,
        string cardHeaderTitle,
        LiveAnalysisPanelKind panelKind,
        IReadOnlyList<string> channels,
        string? initialChannel,
        Action<LiveAnalysisCardViewModel>? onChannelChanged,
        LiveRbpAnalysisViewModel? rbpAnalysis = null,
        LivePsdAnalysisViewModel? psdAnalysis = null,
        LiveStftAnalysisViewModel? stftAnalysis = null)
    {
        AlgorithmId = algorithmId;
        DisplayName = displayName;
        CardHeaderTitle = cardHeaderTitle;
        PanelKind = panelKind;
        this.channels = channels;
        selectedChannel = initialChannel;
        this.onChannelChanged = onChannelChanged;
        RbpAnalysis = rbpAnalysis;
        PsdAnalysis = psdAnalysis;
        StftAnalysis = stftAnalysis;

        CloseAnalysisCommand = new AsyncRelayCommand(() => owner.CloseSlotAsync(AlgorithmId), owner.ReportCommandError);
        DockCommand = new RelayCommand(() => owner.ToggleFloatingSlot(AlgorithmId));
        MaximizeCommand = new RelayCommand(() => owner.ToggleMaximizeSlot(AlgorithmId));
        FocusCommand = new RelayCommand(() => owner.FocusSlot(AlgorithmId));
    }

    public string AlgorithmId { get; }
    public string DisplayName { get; }
    public string CardHeaderTitle { get; }
    public LiveAnalysisPanelKind PanelKind { get; }
    public LiveAnalysisPanelKind ActiveAnalysisPanelKind => PanelKind;

    public LiveRbpAnalysisViewModel? RbpAnalysis { get; }
    public LivePsdAnalysisViewModel? PsdAnalysis { get; }
    public LiveStftAnalysisViewModel? StftAnalysis { get; }

    public ICommand CloseAnalysisCommand { get; }
    public ICommand DockCommand { get; }
    public ICommand MaximizeCommand { get; }
    public ICommand FocusCommand { get; }

    public IReadOnlyList<string> Channels
    {
        get => channels;
        internal set => SetProperty(ref channels, value);
    }

    public string? SelectedChannel
    {
        get => selectedChannel;
        set
        {
            if (!SetProperty(ref selectedChannel, value) || IsClosed) return;
            onChannelChanged?.Invoke(this);
        }
    }

    public string StatusText
    {
        get => statusText;
        internal set => SetProperty(ref statusText, value);
    }

    public bool CanMaximize => !IsFloating;

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

    public string DockToolTip => IsFloating ? "停靠回主工作区" : "弹出为独立窗口";
    public string MaximizeToolTip => IsMaximized ? "还原为自动网格布局" : "放大独占右侧分析区";

    internal Guid SessionId { get; set; }
    internal long Revision { get; set; } = -1;
    internal long SecondaryRevision { get; set; } = -1;
    internal IReadOnlyList<string> ActiveBackendIds { get; set; } = [];
    internal string? LastAppliedConfigKey { get; set; }
    internal bool IsClosed { get; set; }

    internal void SetSelectedChannelSilently(string? channel) =>
        SetProperty(ref selectedChannel, channel, nameof(SelectedChannel));
}
