using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BrainPlatform.Desktop.Shared.Controls;

namespace BrainPlatform.Desktop.Tests.Shared;

public sealed class ColorPickerTests
{
    [Fact]
    public void BoundColorSupportsPreviewCancelApplyExternalUpdatesAndDisable()
    {
        OnSta(() =>
        {
            var draft = new ColorDraft();
            var picker = new ColorPicker { DataContext = draft };
            picker.SetBinding(ColorPicker.SelectedColorProperty, new Binding(nameof(ColorDraft.Color))
            {
                Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            var open = (Button)picker.FindName("OpenButton");
            var popup = (System.Windows.Controls.Primitives.Popup)picker.FindName("CustomColorPopup");
            var hex = (TextBox)picker.FindName("CustomHex");
            var apply = (Button)picker.FindName("ApplyButton");
            Assert.Equal("#112233", picker.SelectedColor);
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            hex.Text = "#ABCDEF";
            Assert.Equal("#112233", draft.Color);
            var cancel = FindButtons((DependencyObject)popup.Child).Single(button => Equals(button.Content, "取消"));
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("#112233", draft.Color);
            Assert.False(popup.IsOpen);

            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            hex.Text = "#ABCDEF";
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("#ABCDEF", draft.Color);
            Assert.NotNull(BindingOperations.GetBinding(picker, ColorPicker.SelectedColorProperty));
            draft.Color = "#445566";
            Assert.Equal("#445566", picker.SelectedColor);
            var brush = Assert.IsType<SolidColorBrush>(((Border)picker.FindName("SelectedPreview")).Background);
            Assert.Equal(Color.FromRgb(0x44, 0x55, 0x66), brush.Color);

            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            hex.Text = "invalid";
            Assert.False(apply.IsEnabled);
            Assert.Equal("#445566", draft.Color);
            picker.IsEnabled = false;
            Assert.False(popup.IsOpen);
            picker.IsCompact = true;
            Assert.Equal(Visibility.Collapsed, ((TextBox)picker.FindName("InlineHex")).Visibility);
        });
    }

    [Fact]
    public void PresetAndRgbSelectionUseTheSameBoundProperty()
    {
        OnSta(() =>
        {
            var picker = new ColorPicker();
            var swatches = (Panel)picker.FindName("InlineSwatches");
            ((Button)swatches.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("#DC2626", picker.SelectedColor);
            ((Button)picker.FindName("OpenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Slider)picker.FindName("RedColorSlider")).Value = 1;
            ((Slider)picker.FindName("GreenColorSlider")).Value = 2;
            ((Slider)picker.FindName("BlueColorSlider")).Value = 3;
            ((Button)picker.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("#010203", picker.SelectedColor);
        });
    }

    [Theory]
    [InlineData("#abcdef", true)]
    [InlineData("#123456", true)]
    [InlineData("#FF123456", false)]
    [InlineData("red", false)]
    [InlineData("#zzzzzz", false)]
    public void RgbFormatIsExplicit(string value, bool valid) => Assert.Equal(valid, RgbColor.TryParse(value, out _));

    private static IEnumerable<Button> FindButtons(DependencyObject node)
    {
        if (node is Button button) yield return button;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var descendant in FindButtons(child)) yield return descendant;
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class ColorDraft : ObservableObject
    {
        private string color = "#112233";
        public string Color { get => color; set => SetProperty(ref color, value); }
    }
}
