using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Modules.Review.Views;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class ReviewCustomFrequencyPanelTests
{
    [Fact]
    public void Frequency_input_arrows_and_keys_step_real_bins_without_crossing_range_bounds() => fixture.Run(() =>
    {
        var model = Model(1);
        var panel = new ReviewCustomFrequencyInspectionPanel { DataContext = new PanelContext(model) };
        var window = Host(panel);
        try
        {
            var point = (TextBox)panel.FindName("FrequencyInput");
            point.ApplyTemplate();
            var steps = (Grid)point.Template.FindName("StepButtons", point);
            var hover = point.Template.Triggers.OfType<Trigger>().Single(t => t.Property == UIElement.IsMouseOverProperty);
            Assert.Contains(hover.Setters.OfType<Setter>(), s => s.TargetName == "StepButtons" && Equals(s.Value, Visibility.Visible));
            Assert.True(steps.IsEnabled);
            var curve = model.PsdCurve;
            Assert.Equal(3, model.SelectedFrequencyHz);
            ClickStep(point, true);
            Assert.Equal(50, model.SelectedFrequencyHz);
            ClickStep(point, true);
            Assert.Equal(50, model.SelectedFrequencyHz);
            ClickStep(point, false);
            Assert.Equal(3, model.SelectedFrequencyHz);
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(point)!, 0, Key.Down)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            point.RaiseEvent(key);
            Assert.True(key.Handled);
            Assert.Equal(2, model.SelectedFrequencyHz);
            Assert.Equal(2.ToString(CultureInfo.CurrentCulture), point.Text);
            point.Text = "invalid";
            ClickStep(point, true);
            Assert.Equal(2, model.SelectedFrequencyHz);
            Assert.True(Validation.GetHasError(point));
            point.Text = "1";
            ClickStep(point, false);
            Assert.Equal(1, model.SelectedFrequencyHz);
            Assert.False(Validation.GetHasError(point));

            model.Mode = ReviewCustomFrequencyMode.Range;
            Drain(window);
            var low = (TextBox)panel.FindName("RangeLowInput");
            var high = (TextBox)panel.FindName("RangeHighInput");
            low.ApplyTemplate();
            high.ApplyTemplate();
            ClickStep(low, true);
            Assert.Equal((2d, 3d), (model.RangeLowHz, model.RangeHighHz));
            ClickStep(low, true);
            ClickStep(high, false);
            Assert.Equal((2d, 3d), (model.RangeLowHz, model.RangeHighHz));
            ClickStep(high, true);
            ClickStep(high, true);
            Assert.Equal((2d, 50d), (model.RangeLowHz, model.RangeHighHz));
            ClickStep(low, false);
            ClickStep(low, false);
            Assert.Equal((1d, 50d), (model.RangeLowHz, model.RangeHighHz));
            Assert.Same(curve, model.PsdCurve);
            model.Clear("等待频率轴");
            Assert.False(((Grid)low.Template.FindName("StepButtons", low)).IsEnabled);
            ClickStep(low, true);
            Assert.Equal(1, model.RangeLowHz);
        }
        finally { window.Close(); }
    });

    private static void ClickStep(TextBox input, bool increase)
    {
        var button = (RepeatButton)input.Template.FindName(increase ? "IncreaseFrequency" : "DecreaseFrequency", input);
        Assert.False(button.Focusable);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
