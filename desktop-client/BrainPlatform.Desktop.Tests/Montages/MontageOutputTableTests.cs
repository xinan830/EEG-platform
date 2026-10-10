using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Shared.Controls;
using BrainPlatform.Desktop.Modules.Montages.Views;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Montages;

[Collection("WPF application resources")]
public sealed class MontageOutputTableTests(WpfApplicationFixture application)
{
    [Fact]
    public void TableBindsColorsOrderAndReadOnlyMode()
        => application.Run(VerifyTable);

    private static void VerifyTable()
    {
        var source = BrainPlatform.Desktop.Tests.Montages.MontageAppearanceTests.Source();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var channels = new ChannelConfigurationWorkspaceViewModel(
            new ChannelMappingViewModel(new ChannelLabelMappingStore(Path.Combine(root, "labels.json"))),
            new ChannelConfigurationProfileStore(Path.Combine(root, "channels.json")));
        channels.Profiles.Add(source);
        var workspace = new MontageConfigurationWorkspaceViewModel(channels,
            new MontageProfileStore(Path.Combine(root, "montages.json")));
        workspace.BeginNewProfile();
        workspace.DraftRows[0].Color = "#DC2626";
        workspace.DraftRows[1].Color = "#16A34A";
        var table = new MontageOutputTable { DataContext = workspace };
        table.Measure(new Size(1_000, 200));
        table.Arrange(new Rect(0, 0, 1_000, 200));
        table.UpdateLayout();
        var picker = FindVisualChild<ColorPicker>(table)!;
        Assert.Equal("#DC2626", picker.SelectedColor);
        picker.SetCurrentValue(ColorPicker.SelectedColorProperty, "#123456");
        Assert.Equal("#123456", workspace.DraftRows[0].Color);
        var firstRow = FindVisualChild<ListBoxItem>(table)!;
        var down = FindVisualChildren<Button>(firstRow).Single(button => Equals(button.ToolTip, "下移"));
        Assert.True(down.Command!.CanExecute(down.CommandParameter));
        down.Command.Execute(down.CommandParameter);
        Assert.Equal("F7", workspace.DraftRows[0].PositiveLabel);
        table.UpdateLayout();

        var evidenceDirectory = Environment.GetEnvironmentVariable("BRAIN_PLATFORM_UI_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            Directory.CreateDirectory(evidenceDirectory);
            foreach (var width in new[] { 900, 1_200 })
            {
                table.Measure(new Size(width, 200));
                table.Arrange(new Rect(0, 0, width, 200));
                table.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, 200, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(table);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(evidenceDirectory, $"montage-table-{width}.png"));
                encoder.Save(stream);
            }
        }

        workspace.SelectedProfile = BrainPlatform.Desktop.Tests.Montages.MontageAppearanceTests.Profile()
            with { Source = MontageProfileSource.System };
        workspace.BeginEditSelected();
        table.UpdateLayout();
        Assert.False(FindVisualChild<ColorPicker>(table)!.IsEnabled);
        workspace.SelectedProfile = BrainPlatform.Desktop.Tests.Montages.MontageAppearanceTests.Profile()
            with { IsRecordingReferenced = true, HasRecordingReferences = true };
        workspace.BeginEditSelected();
        table.UpdateLayout();
        Assert.False(FindVisualChild<ColorPicker>(table)!.IsEnabled);
        Assert.Contains("历史记录", workspace.DraftPermissionText);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject =>
        FindVisualChildren<T>(parent).FirstOrDefault();
}
