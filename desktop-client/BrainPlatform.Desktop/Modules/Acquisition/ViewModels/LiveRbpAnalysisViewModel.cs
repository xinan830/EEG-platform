using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;
using BrainPlatform.Desktop.Shared.Controls.Topomap;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public sealed class LiveRbpAnalysisViewModel : ObservableObject
{
    private string statusText = "实时 RBP 已关闭";
    private string windowText = "等待实时窗口";
    private Guid? sessionId;
    private long latestRevision = -1;
    private string selectedChart = "堆叠历史图";
    private string selectedTopomapPowerType = "relative";
    private string selectedTopomapBandId = "";
    private IReadOnlyList<TopomapField> topomapFields = [];
    private double? topomapStartSeconds;
    private double? topomapEndSeconds;
    private double? visibleStartSeconds;

    public ObservableCollection<RbpChannelHistory> ChannelHistories { get; } = [];
    public ObservableCollection<LiveTopomapBandDisplayItem> TopomapBands { get; } = [];
    public TopomapColorScaleState TopomapColorScales { get; } = new();
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
    public IReadOnlyList<LiveTopomapPowerTypeOption> TopomapPowerTypeOptions { get; } =
        [new("relative", "相对功率"), new("absolute", "绝对功率")];
    public IReadOnlyList<LiveTopomapBandOption> TopomapBandOptions { get; } =
        [new("", "全部频段"), new("delta", "Delta 波"), new("theta", "Theta 波"),
         new("alpha", "Alpha 波"), new("beta", "Beta 波"), new("gamma", "Gamma 波")];
    public string SelectedTopomapPowerType
    {
        get => selectedTopomapPowerType;
        set
        {
            if (!SetProperty(ref selectedTopomapPowerType, value)) return;
            RebuildTopomapBands();
            RaisePropertyChanged(nameof(SelectedTopomapPowerUnit));
            RaisePropertyChanged(nameof(SelectedTopomapField));
        }
    }
    public string SelectedTopomapPowerUnit => SelectedTopomapPowerType == "absolute" ? "uV^2" : "ratio";
    public string SelectedTopomapBandId
    {
        get => selectedTopomapBandId;
        set
        {
            if (!SetProperty(ref selectedTopomapBandId, value)) return;
            RaisePropertyChanged(nameof(SelectedTopomapField));
            RaisePropertyChanged(nameof(IsAllTopomapBandsSelected));
        }
    }
    public TopomapField? SelectedTopomapField => topomapFields.FirstOrDefault(item =>
        !string.IsNullOrEmpty(SelectedTopomapBandId) &&
        string.Equals(item.FieldId, $"rbp.{SelectedTopomapPowerType}.{SelectedTopomapBandId}", StringComparison.OrdinalIgnoreCase));
    public bool IsAllTopomapBandsSelected => string.IsNullOrEmpty(SelectedTopomapBandId);
    public string TopomapWindowText => topomapStartSeconds is { } start && topomapEndSeconds is { } end
        ? $"{start:0.###}–{end:0.###} s" : "分析范围不可用";
    public double? VisibleStartSeconds { get => visibleStartSeconds; private set => SetProperty(ref visibleStartSeconds, value); }
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string WindowText { get => windowText; private set => SetProperty(ref windowText, value); }

    public void Begin(Guid acquisitionSessionId, double? visibleStartSeconds = null)
    {
        sessionId = acquisitionSessionId;
        VisibleStartSeconds = visibleStartSeconds;
        latestRevision = -1;
        ChannelHistories.Clear();
        ClearTopomap();
        StatusText = "等待实时分析窗口";
        WindowText = "预热中：等待完整窗口";
    }

    public void Clear(string status = "实时 RBP 已关闭")
    {
        sessionId = null;
        VisibleStartSeconds = null;
        latestRevision = -1;
        ChannelHistories.Clear();
        ClearTopomap();
        StatusText = status;
        WindowText = "等待实时窗口";
    }

    public bool Apply(LiveRbpUpdate update)
    {
        if (sessionId is null || update.SessionId != sessionId) return false;
        if (update.State == "unavailable" && update.Revision == 0)
        {
            ChannelHistories.Clear();
            StatusText = "实时分析不可用";
            WindowText = update.Message;
            return true;
        }
        if (update.Revision <= latestRevision) return false;
        latestRevision = update.Revision;
        if (update.State == "stopped")
        {
            Clear(update.Message);
            return true;
        }

        StatusText = update.State switch
        {
            "ready" => "",
            "partial" => $"实时窗口存在质量警告 · {update.ScientificIdentity ?? "RBP"}",
            "warming_up" => "预热中：等待完整窗口",
            "unavailable" => "当前窗口不可用",
            _ => update.State,
        };
        if (update.StartSeconds is not { } start || update.EndSeconds is not { } end ||
            !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start || update.Channels is not { Count: > 0 })
        {
            WindowText = update.Message;
            return true;
        }

        WindowText = $"{start:0.###}–{end:0.###} s";
        var channelNames = update.Channels.Select(item => item.Channel).ToArray();
        if (!ChannelHistories.Select(item => item.Channel).SequenceEqual(channelNames, StringComparer.OrdinalIgnoreCase))
        {
            ChannelHistories.Clear();
            foreach (var channel in channelNames)
                ChannelHistories.Add(new RbpChannelHistory(channel, [], RbpResultPreview.BandDefinitions));
        }

        var byChannel = ChannelHistories.ToDictionary(item => item.Channel, StringComparer.OrdinalIgnoreCase);
        foreach (var channel in update.Channels)
        {
            var quality = channel.Quality;
            var state = quality switch
            {
                "unavailable" => "Unavailable",
                "warning" or "partial" => "Partial",
                _ => update.State == "unavailable" ? "Unavailable" : "Complete",
            };
            var shares = state == "Unavailable" ? new double?[RbpResultPreview.BandDefinitions.Count] : channel.Shares;
            var failure = state == "Unavailable" ? channel.Failure : "";
            byChannel[channel.Channel].Append(new RbpWindowPoint(
                end, start, end, state, quality, failure, shares, channel.Warning), RbpResultPreview.MaximumVisitedWindows);
        }
        return true;
    }

    public void ApplyTopomap(IReadOnlyList<TopomapField> fields, double? startSeconds, double? endSeconds)
    {
        topomapFields = fields;
        topomapStartSeconds = startSeconds;
        topomapEndSeconds = endSeconds;
        RebuildTopomapBands();
        RaisePropertyChanged(nameof(SelectedTopomapField));
        RaisePropertyChanged(nameof(TopomapWindowText));
    }

    public void ClearTopomap()
    {
        topomapFields = [];
        topomapStartSeconds = null;
        topomapEndSeconds = null;
        TopomapBands.Clear();
        TopomapColorScales.Reset();
        RaisePropertyChanged(nameof(SelectedTopomapField));
        RaisePropertyChanged(nameof(TopomapWindowText));
    }

    private void RebuildTopomapBands()
    {
        var fields = topomapFields.Where(field => field.FieldId.StartsWith($"rbp.{SelectedTopomapPowerType}.", StringComparison.OrdinalIgnoreCase)).ToArray();
        for (var index = 0; index < fields.Length; index++)
        {
            if (index < TopomapBands.Count) TopomapBands[index].Field = fields[index];
            else TopomapBands.Add(new LiveTopomapBandDisplayItem(fields[index]));
        }
        while (TopomapBands.Count > fields.Length) TopomapBands.RemoveAt(TopomapBands.Count - 1);
    }
}

public sealed record LiveTopomapBandOption(string Id, string Label)
{
    public override string ToString() => Label;
}

public sealed record LiveTopomapPowerTypeOption(string Id, string Label)
{
    public override string ToString() => Label;
}

public sealed class LiveTopomapBandDisplayItem(TopomapField field) : ObservableObject
{
    private TopomapField currentField = field;

    public TopomapField Field
    {
        get => currentField;
        set => SetProperty(ref currentField, value);
    }
}
