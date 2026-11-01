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

    public IReadOnlyList<RbpBandPoint> Bands => preview.BandsAt(cursorSeconds);
    public IReadOnlyList<RbpWindowPoint> Trend => preview.Windows
        .Where(window => window.EndSeconds <= cursorSeconds + 1e-9).ToArray();
    public IReadOnlyList<RbpChannelHistory> ChannelHistories => channelHistories;
    public bool HasResult => Bands.Any(band => band.Share is not null);
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string WindowText { get => windowText; private set => SetProperty(ref windowText, value); }

    public void SetStatus(string value) => StatusText = value;

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
