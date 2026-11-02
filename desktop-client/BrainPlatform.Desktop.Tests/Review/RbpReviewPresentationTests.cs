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

    [Fact]
    public void Topomap_payload_contains_all_bands_without_client_side_selection()
    {
        var model = new RbpReviewAnalysisViewModel();
        var bands = new[] { "delta", "theta", "alpha", "beta", "gamma" }
            .Select(id => new TopomapBand(id, "ratio", [], null, "ready", [], [], null, null)).ToArray();
        var response = new TopomapResponse("window-1", "recording", 5, 10,
            "ready", "rbp-v2", "build", "mne.colin27_1020", "mri", "m",
            bands, null, null);
        model.ApplyTopomap(response);

        Assert.Same(response, model.Topomap);
        Assert.Equal(5, model.Topomap!.Bands.Count);
        Assert.True(model.IsAllTopomapBandsSelected);
        Assert.Contains("标准模板位置", model.TopomapStatusText);
        Assert.Contains("5", model.TopomapWindowText);

        var firstBandSlot = model.TopomapBands[0];
        var nextBands = bands.Select(band => band with { Points = new List<TopomapPoint>() }).ToArray();
        var next = response with
        {
            RunId = "window-2",
            WindowStartSeconds = 10,
            WindowEndSeconds = 15,
            Bands = nextBands,
        };
        Assert.True(model.ApplyTopomapIfAvailable(next));
        Assert.Same(firstBandSlot, model.TopomapBands[0]);
        Assert.Same(nextBands[0], firstBandSlot.Band);
        Assert.False(model.ApplyTopomapIfAvailable(null));
        Assert.Same(next, model.Topomap);

        model.ClearTopomap();
        Assert.Empty(model.TopomapBands);
        Assert.Null(model.Topomap);
    }

    [Fact]
    public void Chart_selector_switches_between_stacked_history_and_topomap_pages()
    {
        var model = new RbpReviewAnalysisViewModel();

        Assert.True(model.IsStackedChartSelected);
        Assert.False(model.IsTopomapSelected);

        model.SelectedChart = "脑电地形图";

        Assert.False(model.IsStackedChartSelected);
        Assert.True(model.IsTopomapSelected);
    }

    [Fact]
    public void Topomap_band_selector_changes_only_the_displayed_band()
    {
        var model = new RbpReviewAnalysisViewModel();
        var bands = new[] { "delta", "theta", "alpha", "beta", "gamma" }
            .Select(id => new TopomapBand(id, "ratio", [], null, "ready", [], [], null, null)).ToArray();
        model.ApplyTopomap(new TopomapResponse("window", "recording", 0, 5, "ready", "v1", "build",
            "mne", "mri", "m", bands, null, null));

        model.SelectedTopomapBandId = "alpha";

        Assert.Equal("alpha", model.SelectedTopomapBand!.BandId);
        Assert.Equal("window", model.Topomap!.RunId);
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
