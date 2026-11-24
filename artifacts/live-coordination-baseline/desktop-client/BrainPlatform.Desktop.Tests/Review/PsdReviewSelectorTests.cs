using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Shared.Controls.Topomap;
using BrainPlatform.Desktop.Tests.Algorithms;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class PsdReviewSelectorTests
{
    [Fact]
    public async Task Topomap_selector_renders_five_bands_and_selects_the_psd_band_field()
    {
        var adapter = new ReviewWindowAnalysisAdapter(
            new OnDemandDetailClient { Respond = OnDemandDetailClient.PsdResponse }, "psd", "PSD", true);
        var request = new AnalysisRunRequest("recording", "official_algorithm",
            System.Text.Json.JsonSerializer.SerializeToElement(new { algorithm_id = "psd", channels = new[] { "F3" } }));
        var response = await adapter.LoadOnDemandAsync("recording", request, 2000, CancellationToken.None);

        WpfTestApplication.Run(() =>
        {
            var model = new PsdReviewAnalysisViewModel();
            model.Apply(response);
            model.SelectedView = "脑电地形图";
            var panel = new PsdReviewAnalysisPanel { DataContext = new { PsdAnalysis = model } };
            Arrange(panel);
            var selector = Descendants<ComboBox>(panel).Single(combo => ReferenceEquals(combo.ItemsSource, model.TopomapBandOptions));
            Assert.DoesNotContain(Descendants<TextBlock>(selector), text => text.Text.Contains(nameof(PsdTopomapBandOption)));

            var expected = new[]
            {
                (Id: "", Label: "全部频段"),
                (Id: "delta", Label: "Delta 波"),
                (Id: "theta", Label: "Theta 波"),
                (Id: "alpha", Label: "Alpha 波"),
                (Id: "beta", Label: "Beta 波"),
                (Id: "gamma", Label: "Gamma 波"),
            };
            Assert.Equal(expected, model.TopomapBandOptions.Select(option => (option.Id, option.Label)));

            foreach (var option in expected.Where(option => option.Id.Length > 0))
            {
                selector.SelectedValue = option.Id;
                Arrange(panel);

                Assert.Equal(option.Id, model.SelectedTopomapBandId);
                Assert.Equal($"psd.band-mean-density.{option.Id}", model.SelectedTopomapFieldId);
                Assert.Contains(Descendants<TextBlock>(selector), text => text.Text == option.Label);
            }

            selector.SelectedValue = "";
            Arrange(panel);
            Assert.True(model.IsAllTopomapBandsSelected);
            Assert.Equal(expected.Skip(1).Select(option => $"psd.band-mean-density.{option.Id}"),
                model.SelectedTopomapFieldIds);

            model.ApplyTopomap(CreateAllBandResponse());
            Arrange(panel);
            var allBandMaps = Descendants<ItemsControl>(panel).Single(control =>
                ReferenceEquals(control.ItemsSource, model.TopomapBands));
            Assert.Equal(Visibility.Visible, allBandMaps.Visibility);
            Assert.Equal(5, allBandMaps.Items.Count);
            Assert.Equal(5, Descendants<TopomapCanvas>(allBandMaps).Count());
        });
    }

    [Fact]
    public void Psd_map_titles_use_concise_band_names()
    {
        var field = new TopomapField("psd.band-mean-density.alpha", "Alpha 平均 PSD (8–13 Hz)", "V^2/Hz",
            null, [], null, "unavailable", [], [], null, null, [8, 13]);

        Assert.Equal("Alpha 波", TopomapCanvas.FieldName(field));
    }

    private static TopomapResponse CreateAllBandResponse()
    {
        var bands = new[] { (Id: "delta", Low: 1d, High: 4d), (Id: "theta", Low: 4d, High: 8d),
            (Id: "alpha", Low: 8d, High: 13d), (Id: "beta", Low: 13d, High: 30d),
            (Id: "gamma", Low: 30d, High: 50d) };
        var fields = bands.Select(band => new TopomapField($"psd.band-mean-density.{band.Id}",
            $"{band.Id} 平均 PSD ({band.Low:g}–{band.High:g} Hz)", "V^2/Hz",
            null, [], null, "unavailable", [], [], null, null, [band.Low, band.High])).ToArray();
        return new TopomapResponse("window", "recording", 10, 20, "unavailable", "psd-v1", "build",
            "montage_position_snapshot", "mri", "m", fields, null, null);
    }

    private static void Arrange(FrameworkElement element)
    {
        element.Measure(new Size(640, 400));
        element.Arrange(new Rect(0, 0, 640, 400));
        element.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
