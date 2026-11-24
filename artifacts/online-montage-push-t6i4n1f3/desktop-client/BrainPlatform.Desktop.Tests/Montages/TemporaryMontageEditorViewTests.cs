using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BrainPlatform.Desktop.Modules.Montages.Views;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Montages;

[Collection("WPF application resources")]
public sealed class TemporaryMontageEditorViewTests(WpfApplicationFixture application)
{
    [Fact]
    public void FullEditorBindsSessionSourcesSaveReferencesAndOutputTable() => application.Run(() =>
    {
        var source = TemporaryMontageFixture.Create("F3", "F4", "Cz").ChannelConfigurationSnapshot;
        var window = new TemporaryMontageEditorWindow(source, null, null);
        var view = Assert.IsType<MontageDetailView>(window.Content);
        var context = Assert.IsType<TemporaryMontageEditorContext>(view.DataContext);
        var editor = context.MontageConfigurations;
        view.Measure(new Size(1280, 900));
        view.Arrange(new Rect(0, 0, 1280, 900));
        view.UpdateLayout();
        Assert.Equal("在线导联", Descendants<TextBox>(view).Single(box => box.Text == "在线导联").Text);
        var sourcePicker = Descendants<ComboBox>(view).Single(box => ReferenceEquals(box.ItemsSource, editor.AvailableChannelConfigurations));
        Assert.Same(source, sourcePicker.SelectedItem);
        Assert.False(sourcePicker.IsEnabled);
        Assert.Same(editor, Assert.Single(Descendants<MontageOutputTable>(view)).DataContext);
        Assert.Contains(Descendants<Button>(view), button => ReferenceEquals(button.Command, editor.SaveDraftCommand));
        Assert.Contains(Descendants<Button>(view), button => ReferenceEquals(button.Command, editor.ApplyAverageReferenceToAllCommand));
        Assert.Equal(3, Descendants<CheckBox>(view).Count(box => box.Content is string label && new[] { "F3", "F4", "Cz" }.Contains(label)));

        var directory = Environment.GetEnvironmentVariable("BRAIN_PLATFORM_UI_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap(1280, 900, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, "online-montage-full-editor.png"));
            encoder.Save(stream);
        }
        window.Close();
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
