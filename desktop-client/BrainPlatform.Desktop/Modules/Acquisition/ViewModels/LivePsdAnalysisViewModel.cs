using System.Collections.ObjectModel;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;
using BrainPlatform.Desktop.Shared.Controls.Topomap;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

/// <summary>Live PSD spectrum and 5-band topomap presentation state matching Review PSD panel semantics.</summary>
public sealed class LivePsdAnalysisViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> TopomapFieldIds =
    [
        "psd.band-mean-density.delta",
        "psd.band-mean-density.theta",
        "psd.band-mean-density.alpha",
        "psd.band-mean-density.beta",
        "psd.band-mean-density.gamma",
    ];

    private readonly PsdPreviewState preview = new() { ValueUnit = "µV²/Hz" };
    private readonly Action<LivePsdAnalysisViewModel>? onViewModeChanged;
    private string selectedView = "频谱";
    private string selectedTopomapBandId = "";
    private string statusText = "等待可用的 10 秒窗口";
    private string windowText = "";
    private string topomapWindowText = "";
    private IReadOnlyList<TopomapField> topomapFields = [];

    public LivePsdAnalysisViewModel(Action<LivePsdAnalysisViewModel>? onViewModeChanged = null)
    {
        this.onViewModeChanged = onViewModeChanged;
    }

    public ObservableCollection<PsdPreviewPoint?> Points => preview.Points;
    public ObservableCollection<LiveTopomapBandDisplayItem> TopomapBands { get; } = [];
    public TopomapColorScaleState TopomapColorScales { get; } = new();
    public string ValueUnit => preview.ValueUnit;
    public bool HasResult => preview.HasPreview;

    public IReadOnlyList<string> ViewOptions { get; } = ["频谱", "脑电地形图"];
    public IReadOnlyList<LiveTopomapBandOption> TopomapBandOptions { get; } =
    [
        new("", "全部频段"),
        new("delta", "Delta 波"),
        new("theta", "Theta 波"),
        new("alpha", "Alpha 波"),
        new("beta", "Beta 波"),
        new("gamma", "Gamma 波"),
    ];

    public string SelectedView
    {
        get => selectedView;
        set
        {
            if (string.IsNullOrEmpty(value) || !SetProperty(ref selectedView, value)) return;
            RaisePropertyChanged(nameof(IsTopomapSelected));
            RaisePropertyChanged(nameof(IsSpectrumSelected));
            RaisePropertyChanged(nameof(CurrentRangeText));
            onViewModeChanged?.Invoke(this);
        }
    }

    public bool IsTopomapSelected => SelectedView == "脑电地形图";
    public bool IsSpectrumSelected => !IsTopomapSelected;

    public string SelectedTopomapBandId
    {
        get => selectedTopomapBandId;
        set
        {
            if (value is null || !TopomapBandOptions.Any(option => option.Id == value)) return;
            if (!SetProperty(ref selectedTopomapBandId, value)) return;
            RaisePropertyChanged(nameof(IsAllTopomapBandsSelected));
            RaisePropertyChanged(nameof(SelectedTopomapField));
        }
    }

    public bool IsAllTopomapBandsSelected => string.IsNullOrEmpty(SelectedTopomapBandId);

    public TopomapField? SelectedTopomapField => IsAllTopomapBandsSelected
        ? null
        : topomapFields.FirstOrDefault(item =>
            string.Equals(item.FieldId, $"psd.band-mean-density.{SelectedTopomapBandId}", StringComparison.OrdinalIgnoreCase));

    public string CurrentRangeText => IsTopomapSelected ? topomapWindowText : windowText;

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public void Begin(string message = "等待可用的 10 秒窗口")
    {
        ClearSpectrum();
        ClearTopomap();
        StatusText = message;
    }

    public void ApplySpectrum(LiveAnalysisUpdate update, string channel, string statusMessage)
    {
        StatusText = statusMessage;
        windowText = FormatRange(update.StartSeconds, update.EndSeconds);
        RaisePropertyChanged(nameof(CurrentRangeText));
        if (update.State is not ("ready" or "partial"))
        {
            ClearSpectrumOnly();
            return;
        }
        if (update.StartSeconds is null || update.EndSeconds is null)
            throw new InvalidOperationException("实时分析缺少窗口时间。");
        LiveSpectralPresentation.ApplyPsd(preview, update, channel);
        RaisePropertyChanged(nameof(HasResult));
    }

    public void ApplyTopomap(LiveAnalysisUpdate update, IReadOnlyList<TopomapField> fields)
    {
        StatusText = update.State is "ready" or "partial" ? "" : update.Message ?? "当前窗口不可用";
        topomapFields = fields;
        topomapWindowText = FormatRange(update.StartSeconds, update.EndSeconds);
        RebuildTopomapBands();
        RaisePropertyChanged(nameof(SelectedTopomapField));
        RaisePropertyChanged(nameof(CurrentRangeText));
    }

    public void SetFault(string message)
    {
        ClearSpectrumOnly();
        ClearTopomap();
        StatusText = message;
    }

    public void ClearSpectrumOnly()
    {
        preview.Clear();
        RaisePropertyChanged(nameof(HasResult));
    }

    public void ClearTopomap()
    {
        topomapFields = [];
        topomapWindowText = "";
        TopomapBands.Clear();
        TopomapColorScales.Reset();
        RaisePropertyChanged(nameof(SelectedTopomapField));
        RaisePropertyChanged(nameof(CurrentRangeText));
    }

    private void ClearSpectrum()
    {
        ClearSpectrumOnly();
        windowText = "";
        RaisePropertyChanged(nameof(CurrentRangeText));
    }

    private void RebuildTopomapBands()
    {
        for (var index = 0; index < topomapFields.Count; index++)
        {
            if (index < TopomapBands.Count) TopomapBands[index].Field = topomapFields[index];
            else TopomapBands.Add(new LiveTopomapBandDisplayItem(topomapFields[index]));
        }
        while (TopomapBands.Count > topomapFields.Count) TopomapBands.RemoveAt(TopomapBands.Count - 1);
    }

    private static string FormatRange(double? start, double? end) =>
        start is { } s && end is { } e ? $"{s:0.###}–{e:0.###} s" : "";
}
