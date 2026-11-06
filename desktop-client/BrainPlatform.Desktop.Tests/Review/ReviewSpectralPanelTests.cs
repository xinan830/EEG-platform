using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BrainPlatform.Desktop.Tests.Shared;
using static BrainPlatform.Desktop.Tests.Review.ReviewSpectralFixture;

namespace BrainPlatform.Desktop.Tests.Review;

[Collection("WPF application resources")]
public sealed class ReviewSpectralPanelTests(WpfApplicationFixture fixture)
{
    [Fact]
    public void Panel_binds_five_bands_and_four_numeric_columns_in_montage_order()
    {
        fixture.Run(() =>
        {
            var psd = Window("psd");
            var rbp = Window("rbp");
            var model = new ReviewSpectralValuesViewModel();
            model.InitializeChannels(ChannelNames);
            model.Apply(psd, rbp, Projection(psd, "psd"), Projection(rbp, "rbp"));
            var panel = new ReviewSpectralValuesPanel { DataContext = new { SpectralValues = model } };
            panel.Measure(new Size(720, 480));
            panel.Arrange(new Rect(0, 0, 720, 480));
            panel.UpdateLayout();
            var grid = Descendants(panel).OfType<DataGrid>().Single();
            var selector = Descendants(panel).OfType<ComboBox>().Single();
            Assert.Equal(5, selector.Items.Count);
            Assert.Equal("alpha", selector.SelectedValue);
            Assert.Equal(5, grid.Columns.Count);
            Assert.True(grid.IsReadOnly);
            Assert.Same(model.Rows, grid.ItemsSource);
            Assert.Equal(ChannelNames, grid.Items.Cast<ReviewSpectralValueRow>().Select(row => row.Channel));
            selector.SelectedValue = "theta";
            Assert.Equal("theta", model.SelectedBandId);
            Assert.Equal(2, model.Rows[0].PsdDensityUv2Hz);
            grid.ScrollIntoView(model.Rows[0]);
            panel.UpdateLayout();
            var container = grid.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.NotNull(container);
            model.SelectedBandId = "beta";
            model.Clear("等待目标窗口");
            model.Apply(psd, rbp, Projection(psd, "psd"), Projection(rbp, "rbp"));
            panel.UpdateLayout();
            Assert.Same(container, grid.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Equal(4, model.Rows[0].PsdDensityUv2Hz);
            model.SelectedBandId = "theta";
            panel.UpdateLayout();
            var image = new RenderTargetBitmap(720, 480, 96, 96, PixelFormats.Pbgra32);
            image.Render(panel);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            var path = Path.GetFullPath("_temp/review-spectral-panel.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = File.Create(path);
            encoder.Save(stream);
        });
    }

    [Fact]
    public void Row_tooltip_wraps_within_shared_width_and_follows_placement_channel_and_band()
    {
        fixture.Run(() =>
        {
            var psd = Window("psd");
            var rbp = Window("rbp");
            var model = new ReviewSpectralValuesViewModel();
            model.InitializeChannels(ChannelNames);
            model.Apply(psd, rbp, Projection(psd, "psd"), Projection(rbp, "rbp"));
            var panel = new ReviewSpectralValuesPanel { DataContext = new { SpectralValues = model } };
            panel.Measure(new Size(720, 480));
            panel.Arrange(new Rect(0, 0, 720, 480));
            panel.UpdateLayout();
            var grid = Descendants(panel).OfType<DataGrid>().Single();
            var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
            var tooltip = Assert.IsType<ToolTip>(row.ToolTip);
            tooltip.PlacementTarget = row;
            tooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tooltip.Arrange(new Rect(tooltip.DesiredSize));
            tooltip.UpdateLayout();

            Assert.Equal(model.Rows[0].Details, tooltip.Content);
            Assert.InRange(tooltip.ActualWidth, 1, 360);
            var body = Descendants(tooltip).OfType<TextBlock>().Single(text => text.Text == model.Rows[0].Details);
            Assert.Equal(TextWrapping.Wrap, body.TextWrapping);
            Assert.True(body.ActualWidth < tooltip.ActualWidth);
            var image = new RenderTargetBitmap((int)Math.Ceiling(tooltip.ActualWidth),
                (int)Math.Ceiling(tooltip.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(tooltip);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            var path = Path.GetFullPath("_temp/review-spectral-tooltip.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = File.Create(path)) encoder.Save(stream);

            var density = Projection(psd, "psd");
            var fields = density.Fields.ToArray();
            fields[2] = fields[2] with { Points = fields[2].Points.Select(point => point with
                { QualityStatus = "warning", QualityReasons = [new string('W', 200)] }).ToArray() };
            model.Apply(psd, rbp, density with { Fields = fields }, Projection(rbp, "rbp"));
            tooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tooltip.Arrange(new Rect(tooltip.DesiredSize));
            tooltip.UpdateLayout();
            Assert.InRange(tooltip.ActualWidth, 1, 360);
            Assert.True(body.ActualHeight > model.Rows[0].Details.Split('\n').Length * body.LineHeight);

            tooltip.PlacementTarget = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(1));
            Assert.Equal(model.Rows[1].Details, tooltip.Content);
            model.SelectedBandId = "theta";
            Assert.Equal(model.Rows[1].Details, tooltip.Content);
            Assert.StartsWith("F7 · Theta", Assert.IsType<string>(tooltip.Content));
            model.Clear("等待目标窗口");
            Assert.Equal("等待目标窗口", tooltip.Content);
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
