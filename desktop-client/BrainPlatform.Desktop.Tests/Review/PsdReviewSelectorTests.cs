using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Review.Analysis;
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
                (Id: "delta", Label: "Delta · 1–4 Hz"),
                (Id: "theta", Label: "Theta · 4–8 Hz"),
                (Id: "alpha", Label: "Alpha · 8–13 Hz"),
                (Id: "beta", Label: "Beta · 13–30 Hz"),
                (Id: "gamma", Label: "Gamma · 30–50 Hz"),
            };
            Assert.Equal(expected, model.TopomapBandOptions.Select(option => (option.Id, option.Label)));

            foreach (var option in expected)
            {
                selector.SelectedValue = option.Id;
                Arrange(panel);

                Assert.Equal(option.Id, model.SelectedTopomapBandId);
                Assert.Equal($"psd.band-mean-density.{option.Id}", model.SelectedTopomapFieldId);
                Assert.Contains(Descendants<TextBlock>(selector), text => text.Text == option.Label);
            }
        });
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
