using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class LiveRbpAnalysisViewModelTests
{
    private static readonly Guid SessionId = Guid.Parse("91ca20ee-a76f-47cb-b9cc-3ef03e77f2d3");

    [Fact]
    public void AppendsPerChannelWindowsAtBackendElapsedTime()
    {
        var viewModel = new LiveRbpAnalysisViewModel();
        viewModel.Begin(SessionId);

        Assert.True(viewModel.Apply(Update(10, 11, 0.1, 1)));
        Assert.True(viewModel.Apply(Update(11, 12, 0.2, 2)));

        var channel = Assert.Single(viewModel.ChannelHistories);
        Assert.Equal("Fz", channel.Channel);
        Assert.Equal(new[] { 11d, 12d }, channel.Windows.Select(window => window.EndSeconds));
        Assert.Equal(new[] { 0.1, 0.2 }, channel.Windows.Select(window => window.Shares[0]!.Value));
    }

    [Fact]
    public void UnavailableWindowIsRenderedAsAnExplicitGap()
    {
        var viewModel = new LiveRbpAnalysisViewModel();
        viewModel.Begin(SessionId);
        viewModel.Apply(Update(10, 11, 0.1, 1));
        viewModel.Apply(new LiveRbpUpdate("unavailable", "采集缺口", 11, 12, "rbp-live-v1",
            [new LiveRbpChannelResult("Fz", [null, null, null, null, null], "unavailable", "", "source_gap")], SessionId, 2));

        var windows = Assert.Single(viewModel.ChannelHistories).Windows;
        Assert.Equal("Unavailable", windows[^1].State);
        Assert.All(windows[^1].Shares, Assert.Null);
        Assert.Equal("source_gap", windows[^1].Failure);
    }

    [Fact]
    public void EndingSessionClearsHistoryAndStatus()
    {
        var viewModel = new LiveRbpAnalysisViewModel();
        viewModel.Begin(SessionId);
        viewModel.Apply(Update(10, 11, 0.1, 1));

        viewModel.Clear();

        Assert.Empty(viewModel.ChannelHistories);
        Assert.Equal("实时 RBP 已关闭", viewModel.StatusText);
        Assert.False(viewModel.Apply(Update(11, 12, 0.2, 2)));
    }

    [Fact]
    public void IgnoresStaleAndPreviousSessionResults()
    {
        var viewModel = new LiveRbpAnalysisViewModel();
        viewModel.Begin(SessionId);
        Assert.True(viewModel.Apply(Update(10, 11, 0.2, 5)));
        Assert.False(viewModel.Apply(Update(9, 10, 0.1, 4)));
        viewModel.Begin(Guid.NewGuid());
        Assert.False(viewModel.Apply(Update(11, 12, 0.3, 6)));

        Assert.Empty(viewModel.ChannelHistories);
    }

    [Fact]
    public void TransportFailureRemovesStaleChartValues()
    {
        var viewModel = new LiveRbpAnalysisViewModel();
        viewModel.Begin(SessionId);
        viewModel.Apply(Update(10, 11, 0.2, 5));

        Assert.True(viewModel.Apply(new LiveRbpUpdate("unavailable", "LIVE_ACCESS_BRIDGE_FAILED", null, null,
            "rbp-live-v1", SessionId: SessionId)));

        Assert.Empty(viewModel.ChannelHistories);
        Assert.Equal("实时分析不可用", viewModel.StatusText);
    }

    private static LiveRbpUpdate Update(double start, double end, double delta, long revision) =>
        new("ready", "", start, end, "rbp-live-v1",
            [new LiveRbpChannelResult("Fz", [delta, 0.2, 0.3, 0.1, 0.3], "clean", "", "")], SessionId, revision);
}
