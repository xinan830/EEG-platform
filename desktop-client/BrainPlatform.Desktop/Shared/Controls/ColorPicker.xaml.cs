using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Shared.Controls;

/// <summary>Reusable color editing. Preview remains local until applied.</summary>
public partial class ColorPicker : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(string), typeof(ColorPicker),
        new FrameworkPropertyMetadata("#2563EB", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (owner, _) => ((ColorPicker)owner).RefreshPresentation()));

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(ColorPicker),
        new PropertyMetadata(false, (owner, _) => ((ColorPicker)owner).RefreshPresentation()));

    private bool updatingPreview;

    public ColorPicker()
    {
        InitializeComponent();
        AddSwatches(InlineSwatches, isPreview: false);
        AddSwatches(PopupSwatches, isPreview: true);
        IsEnabledChanged += (_, _) => { if (!IsEnabled) CustomColorPopup.IsOpen = false; };
        RefreshPresentation();
    }

    public string SelectedColor
    {
        get => (string)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    private void AddSwatches(Panel panel, bool isPreview)
    {
        (string Color, string Label)[] swatches =
        [
            ("#DC2626", "红色"), ("#16A34A", "绿色"), ("#2563EB", "蓝色"),
            ("#EA580C", "橙色"), ("#7C3AED", "紫色"), ("#06B6D4", "青色"),
            ("#EC4899", "粉色"), ("#F59E0B", "黄色"), ("#0D9488", "墨绿"),
        ];
        foreach (var (color, label) in swatches)
        {
            RgbColor.TryParse(color, out var rgb);
            var button = new Button
            {
                Tag = color, Background = new SolidColorBrush(rgb), ToolTip = label,
                Style = (Style)Resources["SwatchStyle"],
            };
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, _) =>
            {
                if (isPreview) SetPreview(rgb);
                else SetCurrentValue(SelectedColorProperty, color);
            };
            panel.Children.Add(button);
        }
    }

    private void RefreshPresentation()
    {
        if (SelectedPreview is null) return;
        InlineSwatches.Visibility = InlineHex.Visibility = IsCompact ? Visibility.Collapsed : Visibility.Visible;
        SelectedPreview.Background = RgbColor.TryParse(SelectedColor, out var color)
            ? new SolidColorBrush(color) : Brushes.Transparent;
        foreach (var button in InlineSwatches.Children.OfType<Button>())
        {
            var selected = string.Equals(button.Tag as string, SelectedColor, StringComparison.OrdinalIgnoreCase);
            button.BorderBrush = selected ? Brushes.DodgerBlue : Brushes.White;
            button.BorderThickness = new Thickness(selected ? 3 : 2);
        }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        SetPreview(RgbColor.TryParse(SelectedColor, out var color) ? color : Colors.RoyalBlue);
        CustomColorPopup.IsOpen = true;
    }

    private void SetPreview(Color color)
    {
        updatingPreview = true;
        RedColorSlider.Value = color.R;
        GreenColorSlider.Value = color.G;
        BlueColorSlider.Value = color.B;
        CustomHex.Text = RgbColor.Format(color);
        updatingPreview = false;
        UpdatePreviewState();
    }

    private void OnRgbChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updatingPreview || CustomHex is null || BlueColorSlider is null) return;
        SetPreview(Color.FromRgb((byte)Math.Round(RedColorSlider.Value),
            (byte)Math.Round(GreenColorSlider.Value), (byte)Math.Round(BlueColorSlider.Value)));
    }

    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (updatingPreview || BlueColorSlider is null) return;
        if (RgbColor.TryParse(CustomHex.Text, out var color)) SetPreview(color);
        else UpdatePreviewState();
    }

    private void UpdatePreviewState()
    {
        var valid = RgbColor.TryParse(CustomHex.Text, out var color);
        ApplyButton.IsEnabled = valid;
        ColorError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        PickerColorPreview.Background = valid ? new SolidColorBrush(color) : Brushes.Transparent;
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || !RgbColor.TryParse(CustomHex.Text, out var color)) return;
        SetCurrentValue(SelectedColorProperty, RgbColor.Format(color));
        CustomColorPopup.IsOpen = false;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => CustomColorPopup.IsOpen = false;
    private void OnPickerUnloaded(object sender, RoutedEventArgs e) => CustomColorPopup.IsOpen = false;
}
