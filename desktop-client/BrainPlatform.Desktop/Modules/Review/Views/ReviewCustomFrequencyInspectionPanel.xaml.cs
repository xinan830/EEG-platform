using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Review.ViewModels;

namespace BrainPlatform.Desktop.Modules.Review.Views;

public partial class ReviewCustomFrequencyInspectionPanel : UserControl
{
    public ReviewCustomFrequencyInspectionPanel() => InitializeComponent();

    private void OnFrequencyInputKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox input) return;
        if (e.Key is Key.Up or Key.Down)
        {
            StepFrequency(input, e.Key == Key.Up);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;
        input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        input.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        e.Handled = true;
    }

    private void OnFrequencyStepClick(object sender, RoutedEventArgs e)
    {
        if (sender is RepeatButton { TemplatedParent: TextBox input } button)
            StepFrequency(input, Equals(button.Tag, "1"));
    }

    private static void StepFrequency(TextBox input, bool increase)
    {
        if (input.DataContext is not ReviewCustomFrequencyInspectionViewModel { HasBins: true } model) return;
        var binding = input.GetBindingExpression(TextBox.TextProperty);
        binding?.UpdateSource();
        if (Validation.GetHasError(input)) return;
        var current = input.Name switch
        {
            "RangeLowInput" => model.RangeLowHz,
            "RangeHighInput" => model.RangeHighHz,
            _ => model.SelectedFrequencyHz,
        };
        if (!double.IsFinite(current)) return;
        var candidates = model.FrequenciesHz.Where(f => (increase ? f > current : f < current) &&
            (input.Name != "RangeLowInput" || f < model.RangeHighHz) &&
            (input.Name != "RangeHighInput" || f > model.RangeLowHz));
        var next = increase ? candidates.DefaultIfEmpty(current).First() : candidates.DefaultIfEmpty(current).Last();
        if (next != current)
        {
            if (input.Name == "RangeLowInput") model.RangeLowHz = next;
            else if (input.Name == "RangeHighInput") model.RangeHighHz = next;
            else model.SelectedFrequencyHz = next;
        }
        binding?.UpdateTarget();
    }
}
