using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>RBP-only presentation projection for the Review panel.</summary>
public sealed class RbpReviewAnalysisViewModel : ObservableObject
{
    private RbpResultPreview preview = RbpResultPreview.Parse(null);
    private IReadOnlyList<RbpChannelHistory> channelHistories = [];
    private string statusText = "RBP 尚未准备。";
    private string windowText = "尚未到达分析窗口";
    private double cursorSeconds;
    private string? windowSession;
    private WindowPreviewResponse? appliedResponse;
    private TopomapResponse? topomap;
    private string selectedChart = "堆叠历史图";
    private string selectedTopomapBandId = "";
    private string selectedTopomapPowerType = "relative";

    public IReadOnlyList<RbpBandPoint> Bands => preview.BandsAt(cursorSeconds);
    public IReadOnlyList<RbpWindowPoint> Trend => preview.Windows
        .Where(window => window.EndSeconds <= cursorSeconds + 1e-9).ToArray();
    public IReadOnlyList<RbpChannelHistory> ChannelHistories => channelHistories;
    public ObservableCollection<TopomapBandDisplayItem> TopomapBands { get; } = [];
    public bool HasResult => Bands.Any(band => band.Share is not null);
    public IReadOnlyList<string> ChartOptions { get; } = ["堆叠历史图", "脑电地形图"];
    public string SelectedChart
    {
        get => selectedChart;
        set
        {
            if (!SetProperty(ref selectedChart, value)) return;
            RaisePropertyChanged(nameof(IsStackedChartSelected));
            RaisePropertyChanged(nameof(IsTopomapSelected));
        }
    }
    public bool IsStackedChartSelected => SelectedChart == "堆叠历史图";
    public bool IsTopomapSelected => SelectedChart == "脑电地形图";
    public TopomapResponse? Topomap { get => topomap; private set => SetProperty(ref topomap, value); }
    public IReadOnlyList<TopomapBandOption> TopomapBandOptions { get; } =
    [
        new("", "全部频段"),
        new("delta", "Delta 波"), new("theta", "Theta 波"), new("alpha", "Alpha 波"),
        new("beta", "Beta 波"), new("gamma", "Gamma 波"),
    ];
    public IReadOnlyList<TopomapPowerTypeOption> TopomapPowerTypeOptions { get; } =
    [
        new("relative", "相对功率"), new("absolute", "绝对功率"),
    ];
    public string SelectedTopomapPowerType
    {
        get => selectedTopomapPowerType;
        set
        {
            if (!SetProperty(ref selectedTopomapPowerType, value)) return;
            RaisePropertyChanged(nameof(SelectedTopomapPowerUnit));
        }
    }
    public string SelectedTopomapPowerUnit => SelectedTopomapPowerType == "absolute" ? "uV^2" : "ratio";
    public string SelectedTopomapBandId
    {
        get => selectedTopomapBandId;
        set
        {
            if (!SetProperty(ref selectedTopomapBandId, value)) return;
            RaisePropertyChanged(nameof(SelectedTopomapBand));
            RaisePropertyChanged(nameof(IsAllTopomapBandsSelected));
        }
    }
    public TopomapBand? SelectedTopomapBand => Topomap?.Bands.FirstOrDefault(band =>
        !string.IsNullOrEmpty(SelectedTopomapBandId) &&
        string.Equals(band.BandId, SelectedTopomapBandId, StringComparison.OrdinalIgnoreCase));
    public bool IsAllTopomapBandsSelected => string.IsNullOrEmpty(SelectedTopomapBandId);
    public string TopomapWindowText => Topomap is { } map
        ? $"{map.WindowStartSeconds:0.###}–{map.WindowEndSeconds:0.###} s"
        : "暂无窗口";
    public string TopomapStatusText => Topomap?.Status == "ready" ? $"标准模板位置 · {TopomapWindowText}" : "暂无地形图";
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string WindowText { get => windowText; private set => SetProperty(ref windowText, value); }

    public void SetStatus(string value) => StatusText = value;

    public void ApplyTopomap(TopomapResponse response)
    {
        if (response is { Bands.Count: > 0 } && !string.IsNullOrEmpty(SelectedTopomapBandId) && !response.Bands.Any(band =>
                string.Equals(band.BandId, SelectedTopomapBandId, StringComparison.OrdinalIgnoreCase)))
            SelectedTopomapBandId = "";
        SetTopomap(response);
    }

    public bool ApplyTopomapIfAvailable(TopomapResponse? response)
    {
        if (response is null) return false;
        ApplyTopomap(response);
        return true;
    }

    public void ClearTopomap() => SetTopomap(null);

    private void SetTopomap(TopomapResponse? response)
    {
        Topomap = response;
        UpdateTopomapBands(response?.Bands ?? Array.Empty<TopomapBand>());
        RaisePropertyChanged(nameof(SelectedTopomapBand));
        RaisePropertyChanged(nameof(TopomapWindowText));
        RaisePropertyChanged(nameof(TopomapStatusText));
    }

    private void UpdateTopomapBands(IReadOnlyList<TopomapBand> bands)
    {
        for (var index = 0; index < bands.Count; index++)
        {
            if (index < TopomapBands.Count)
                TopomapBands[index].Band = bands[index];
            else
                TopomapBands.Add(new TopomapBandDisplayItem(bands[index]));
        }
        while (TopomapBands.Count > bands.Count)
            TopomapBands.RemoveAt(TopomapBands.Count - 1);
    }

    public void Apply(WindowPreviewResponse? response, double cursorSeconds)
    {
        if (ReferenceEquals(response, appliedResponse) && this.cursorSeconds == cursorSeconds) return;
        this.cursorSeconds = cursorSeconds;
        if (response is null)
        {
            preview = RbpResultPreview.Parse(null);
            appliedResponse = null;
            channelHistories = [];
            RaisePropertyChanged(nameof(ChannelHistories));
            windowSession = null;
            WindowText = "尚未到达分析窗口";
            RaiseChanged();
            ClearTopomap();
            return;
        }

        var previous = windowSession == response.RunId && appliedResponse?.RecordingId == response.RecordingId ? preview : null;
        preview = RbpResultPreview.FromReviewWindow(response, previous);
        appliedResponse = response;
        windowSession = response.RunId;
        var channels = preview.ChannelWindows.Select(point => point.Channel).Distinct(StringComparer.OrdinalIgnoreCase);
        if (previous is null || !channelHistories.Select(history => history.Channel)
                .SequenceEqual(channels, StringComparer.OrdinalIgnoreCase))
        {
            channelHistories = RbpHistoryProjection.FromVisited(preview.ChannelWindows);
            RaisePropertyChanged(nameof(ChannelHistories));
        }
        else if (response.Status == "available")
        {
            var current = preview.ChannelWindows.Where(point =>
                point.Window.StartSeconds == response.WindowStartSeconds &&
                point.Window.EndSeconds == response.WindowEndSeconds);
            RbpHistoryProjection.AppendVisited(channelHistories, current, RbpResultPreview.MaximumVisitedWindows);
        }
        var latest = preview.Windows.LastOrDefault(window => window.EndSeconds <= cursorSeconds + 1e-9);
        WindowText = latest is null
            ? "尚未到达分析窗口"
            : $"{latest.StartSeconds:0.###}–{latest.EndSeconds:0.###} s · {latest.State}";
        StatusText = response.Status switch
        {
            "available" => "RBP 窗口已更新。",
            "insufficient_data" => "当前 RBP 窗口数据不足。",
            "rejected" => "当前 RBP 窗口被质量规则拒绝。",
            "unavailable" => "当前 RBP 窗口不可用。",
            _ => $"RBP 窗口状态：{response.Status}",
        };
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        RaisePropertyChanged(nameof(Bands));
        RaisePropertyChanged(nameof(Trend));
        RaisePropertyChanged(nameof(HasResult));
    }
}

public sealed record TopomapBandOption(string Id, string Label)
{
    public override string ToString() => Label;
}

public sealed record TopomapPowerTypeOption(string Id, string Label)
{
    public override string ToString() => Label;
}

public sealed class TopomapBandDisplayItem(TopomapBand band) : ObservableObject
{
    private TopomapBand band = band;

    public TopomapBand Band
    {
        get => band;
        set => SetProperty(ref band, value);
    }
}
