using System.Collections.Specialized;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.ViewModels;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RbpReviewPresentationTests
{
    [Fact]
    public void New_window_preserves_channel_graph_and_appends_once_per_channel()
    {
        var model = new RbpReviewAnalysisViewModel();
        model.Apply(Ready(5), 5);
        var histories = model.ChannelHistories;
        var changes = new List<string?>();
        model.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        var collections = 0;
        foreach (var history in histories)
            history.Windows.CollectionChanged += (_, e) =>
            {
                Assert.Equal(NotifyCollectionChangedAction.Add, e.Action);
                collections++;
            };

        model.Apply(Ready(6), 6);

        Assert.Same(histories, model.ChannelHistories);
        Assert.DoesNotContain(nameof(model.ChannelHistories), changes);
        Assert.Equal(21, collections);
        Assert.All(histories, history => Assert.Equal(new[] { 5d, 6d }, history.Windows.Select(w => w.EndSeconds)));
    }

    [Fact]
    public void Reapplying_same_response_does_not_touch_history_or_notify_charts()
    {
        var model = new RbpReviewAnalysisViewModel();
        var response = Ready(5);
        model.Apply(response, 5);
        var changes = 0;
        model.PropertyChanged += (_, _) => changes++;
        foreach (var history in model.ChannelHistories)
            history.Windows.CollectionChanged += (_, _) => changes++;

        model.Apply(response, 5);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Backfill_replaces_actual_window_and_new_session_or_detach_clears_history()
    {
        var model = new RbpReviewAnalysisViewModel();
        model.Apply(Ready(5), 5);
        model.Apply(Ready(7), 7);
        var histories = model.ChannelHistories;
        model.Apply(Ready(5, alpha: 0.5), 5);

        Assert.Same(histories, model.ChannelHistories);
        Assert.All(histories, history =>
        {
            Assert.Equal(2, history.Windows.Count);
            Assert.Equal(0.5, history.Windows[0].Shares[2]);
            Assert.Equal(0.6, history.Windows[1].Shares[2]);
        });
        model.Apply(Ready(9) with { RunId = "new-session" }, 9);
        Assert.NotSame(histories, model.ChannelHistories);
        Assert.All(model.ChannelHistories, history => Assert.Single(history.Windows));
        model.Apply(null, 0);
        Assert.Empty(model.ChannelHistories);
        Assert.False(model.HasResult);
    }

    internal static WindowPreviewResponse Ready(double end, double alpha = 0.6)
    {
        var channels = Enumerable.Range(1, 21).Select(i => $"CH{i}").ToArray();
        string[] names = ["delta", "theta", "alpha", "beta", "gamma"];
        double[] shares = [0.1, 0.1, alpha, 0.15, 0.65 - alpha];
        return new WindowPreviewResponse("session", "recording", (long)(end * 200), end,
            "available", [], new WindowPreviewWindow($"w-{end}", 0,
                (long)((end - 5) * 200), (long)(end * 200), "ready", "clean", []), false, null, null,
            new Dictionary<string, JsonElement>
            {
                ["time_s"] = JsonSerializer.SerializeToElement(new[] { end }),
                ["channel_index"] = JsonSerializer.SerializeToElement(Enumerable.Range(0, channels.Length).ToArray()),
            }, null,
            names.Select((name, index) => (name, index)).ToDictionary(p => p.name,
                p => JsonSerializer.SerializeToElement(Enumerable.Repeat(shares[p.index], channels.Length).ToArray())),
            names.ToDictionary(name => name, _ => JsonSerializer.SerializeToElement(new { unit = "ratio" })),
            "rbp-fixture", "fixture", Evidence: JsonSerializer.SerializeToElement(new
            {
                extensions = new { channel_order = new { channels } },
            }), WindowStartSeconds: end - 5, WindowEndSeconds: end);
    }
}
