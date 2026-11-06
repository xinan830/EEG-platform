using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using static BrainPlatform.Desktop.Tests.Review.ReviewSpectralFixture;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewCustomFrequencyInspectionViewModelTests
{
    [Fact]
    public void Single_frequency_snaps_to_backend_axis_and_channel_selection_changes_only_the_curve()
    {
        var model = new ReviewCustomFrequencyInspectionViewModel();
        model.InitializeChannels(ChannelNames);
        model.SelectFrequency(2.6);
        var rows = model.Rows.ToArray();
        var psd = Window("psd");
        var rbp = Window("rbp");

        model.ApplyBins(psd, rbp, Bins());

        Assert.Equal(3.0, model.ActualFrequencyHz);
        Assert.Equal(2, model.SelectedBinIndex);
        Assert.Equal(30, rows[0].PsdDensityUv2PerHz);
        Assert.Equal(9, rows[0].AbsolutePowerUv2);
        Assert.Equal(3, rows[0].AmplitudeRmsUv);
        Assert.Equal(9, rows[0].RelativePowerPercent);
        Assert.Equal(ChannelNames[0], model.SelectedChannel);
        Assert.Equal(new double?[] { 10, 20, 30, 40 }, model.PsdCurve.Select(point => point.DensityUv2PerHz));

        model.SelectedChannel = ChannelNames[1];

        Assert.Equal(new double?[] { 11, 21, 31, 41 }, model.PsdCurve.Select(point => point.DensityUv2PerHz));
        for (var index = 0; index < rows.Length; index++) Assert.Same(rows[index], model.Rows[index]);
        Assert.Equal(30, model.Rows[0].PsdDensityUv2PerHz);
    }

    [Fact]
    public void Range_mode_clears_old_point_values_and_applies_only_matching_window_result()
    {
        var model = new ReviewCustomFrequencyInspectionViewModel();
        model.InitializeChannels(ChannelNames);
        var psd = Window("psd");
        var rbp = Window("rbp");
        var bins = Bins();
        model.ApplyBins(psd, rbp, bins);
        var row = model.Rows[0];

        model.Mode = ReviewCustomFrequencyMode.Range;

        Assert.Null(row.AbsolutePowerUv2);
        Assert.True(model.IsRangeValid);
        model.ApplyRange(new CustomFrequencyRangeResponse("range", "registered", 5000, 10000,
            1, 3, [
                new(ChannelNames[0], "clean", [], "clean", [], 100, 2, 5, 2, 2),
                new(ChannelNames[1], "clean", [], "clean", [], 200, 4, 10, 2, 3),
            ], bins.PsdProvenance, bins.RbpProvenance));

        Assert.Equal(2, row.PsdDensityUv2PerHz);
        Assert.Equal(5, row.AbsolutePowerUv2);
        Assert.Equal(100, row.RbpDenominatorUv2);
        Assert.Contains("1–50 Hz 总功率", row.Details);
        Assert.Throws<InvalidOperationException>(() => model.ApplyRange(new CustomFrequencyRangeResponse(
            "range", "registered", 5000, 10000, 1, 3, [], bins.PsdProvenance,
            bins.RbpProvenance with { WindowId = "stale-rbp" })));

        model.RangeLowHz = 3;
        model.RangeHighHz = 2;
        Assert.False(model.IsRangeValid);
        model.Clear("窗口变化");
        Assert.Same(row, model.Rows[0]);
        Assert.Null(row.PsdDensityUv2PerHz);
    }

    [Fact]
    public void Range_endpoints_snap_to_the_current_axis_on_input_and_when_bins_arrive()
    {
        var model = new ReviewCustomFrequencyInspectionViewModel();
        model.InitializeChannels(ChannelNames);
        model.Mode = ReviewCustomFrequencyMode.Range;
        model.RangeLowHz = 1.6;
        model.RangeHighHz = 3.6;

        model.ApplyBins(Window("psd"), Window("rbp"), Bins());

        Assert.Equal(2, model.RangeLowHz);
        Assert.Equal(4, model.RangeHighHz);
        Assert.True(model.IsRangeValid);

        model.RangeLowHz = 2.5;
        Assert.Equal(2, model.RangeLowHz);
        model.RangeHighHz = 3.6;
        Assert.Equal(4, model.RangeHighHz);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4.1)]
    [InlineData(double.NaN)]
    public void Range_endpoints_outside_the_current_axis_remain_invalid(double highHz)
    {
        var model = new ReviewCustomFrequencyInspectionViewModel();
        model.InitializeChannels(ChannelNames);
        model.ApplyBins(Window("psd"), Window("rbp"), Bins());
        model.Mode = ReviewCustomFrequencyMode.Range;

        model.RangeHighHz = highHz;

        Assert.Equal(highHz, model.RangeHighHz);
        Assert.False(model.IsRangeValid);
    }

    private static CustomFrequencyBinsResponse Bins()
    {
        var psd = Provenance("psd-10000");
        var rbp = Provenance("rbp-10000");
        return new CustomFrequencyBinsResponse("bins", "registered", 5000, 10000,
            [1, 2, 3, 4], [new(1, 1.5), new(1.5, 2.5), new(2.5, 3.5), new(3.5, 4)],
            [
                new(ChannelNames[0], "clean", [], "clean", [], 100, [10, 20, 30, 40],
                    [1, 4, 9, 16], [1, 4, 9, 16], [1, 2, 3, 4]),
                new(ChannelNames[1], "clean", [], "clean", [], 200, [11, 21, 31, 41],
                    [1, 4, 9, 16], [0.5, 2, 4.5, 8], [1, 2, 3, 4]),
            ], psd, rbp);
    }

    private static CustomFrequencyProvenance Provenance(string windowId) =>
        new(windowId, "fixture-v1", "impl", Window(windowId.StartsWith("psd") ? "psd" : "rbp")
            .BackendIdentity!.Value.GetProperty("configuration_sha256").GetString()!, "policy");

    [Theory]
    [InlineData(0)]
    [InlineData(50.1)]
    [InlineData(double.NaN)]
    public void Out_of_domain_point_is_unavailable_and_does_not_silently_clip(double frequency)
    {
        var model = new ReviewCustomFrequencyInspectionViewModel();
        model.InitializeChannels(ChannelNames);
        model.ApplyBins(Window("psd"), Window("rbp"), Bins());
        model.SelectFrequency(frequency);
        Assert.False(model.IsFrequencyValid);
        Assert.Null(model.ActualFrequencyHz);
        Assert.All(model.Rows, row => Assert.Null(row.AbsolutePowerUv2));
        model.SelectFrequency(2);
        Assert.Equal(2, model.ActualFrequencyHz);
        Assert.NotNull(model.Rows[0].AbsolutePowerUv2);
    }

    [Theory]
    [InlineData("axis")]
    [InlineData("interval")]
    [InlineData("dimensions")]
    [InlineData("nonfinite")]
    [InlineData("channels")]
    public void Malformed_bin_contract_is_rejected_before_publishing_rows(string damage)
    {
        var model = new ReviewCustomFrequencyInspectionViewModel();
        model.InitializeChannels(ChannelNames);
        var value = Bins();
        value = damage switch
        {
            "axis" => value with { FrequencyHz = [1, 3, 2, 4] },
            "interval" => value with { BinIntervals = [new(1, 0), new(1.5, 2.5), new(2.5, 3.5), new(3.5, 4)] },
            "dimensions" => value with { Channels = [value.Channels[0] with { AmplitudeRmsUv = [1] }, value.Channels[1]] },
            "nonfinite" => value with { Channels = [value.Channels[0] with { AmplitudeRmsUv = [1, 2, double.NaN, 4] }, value.Channels[1]] },
            _ => value with { Channels = value.Channels.Reverse().ToArray() },
        };

        Assert.Throws<InvalidOperationException>(() => model.ApplyBins(Window("psd"), Window("rbp"), value));
        Assert.False(model.HasBins);
        Assert.All(model.Rows, row => Assert.Null(row.AbsolutePowerUv2));
    }

    [Fact]
    public void Sorting_the_table_preserves_source_order_and_backend_unavailable_values()
    {
        var model = new ReviewCustomFrequencyInspectionViewModel();
        model.InitializeChannels(ChannelNames);
        var value = Bins();
        value = value with { Channels = [value.Channels[0], value.Channels[1] with
        {
            PsdQuality = "unavailable", PsdQualityReasons = ["missing_samples"],
            PsdDensityUv2PerHz = [null, null, null, null],
        }] };
        model.ApplyBins(Window("psd"), Window("rbp"), value);
        var view = new System.Windows.Data.ListCollectionView(model.Rows);
        view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(ReviewCustomFrequencyRow.Channel),
            System.ComponentModel.ListSortDirection.Descending));
        model.SelectedChannel = ((ReviewCustomFrequencyRow)view.GetItemAt(0)).Channel;
        model.SelectFrequency(1.5);

        Assert.Equal(ChannelNames, model.Rows.Select(row => row.Channel));
        Assert.Equal(1, model.ActualFrequencyHz);
        Assert.All(model.PsdCurve, point => Assert.Null(point.DensityUv2PerHz));
        Assert.Null(model.Rows[1].PsdDensityUv2PerHz);
        Assert.NotNull(model.Rows[1].AbsolutePowerUv2);
        Assert.Contains("missing_samples", model.Rows[1].Details);
    }
}
