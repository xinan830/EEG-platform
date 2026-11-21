using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public sealed class LiveRbpAnalysisViewModel : ObservableObject
{
    private string statusText = "实时 RBP 已关闭";
    private string windowText = "等待实时窗口";
    private Guid? sessionId;
    private long latestRevision = -1;

    public ObservableCollection<RbpChannelHistory> ChannelHistories { get; } = [];
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string WindowText { get => windowText; private set => SetProperty(ref windowText, value); }

    public void Begin(Guid acquisitionSessionId)
    {
        sessionId = acquisitionSessionId;
        latestRevision = -1;
        ChannelHistories.Clear();
        StatusText = "等待实时分析窗口";
        WindowText = "预热中：等待完整窗口";
    }

    public void Clear(string status = "实时 RBP 已关闭")
    {
        sessionId = null;
        latestRevision = -1;
        ChannelHistories.Clear();
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
            "ready" => $"实时窗口已更新 · {update.ScientificIdentity ?? "RBP"}",
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
}
