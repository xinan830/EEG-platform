using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

/// <summary>
/// Unified 2D / 3D EEG topomap viewer used by Review and Live Acquisition panels.
/// Defaults to 2D TopomapCanvas and lazily activates WebGL2 Topomap3DWebView when switched to 3D.
/// </summary>
public partial class TopomapSwitchableView : UserControl
{
    public static readonly DependencyProperty FieldProperty = DependencyProperty.Register(
        nameof(Field), typeof(TopomapField), typeof(TopomapSwitchableView),
        new PropertyMetadata(null, OnFieldOrScaleChanged));

    public static readonly DependencyProperty ColorScaleStateProperty = DependencyProperty.Register(
        nameof(ColorScaleState), typeof(TopomapColorScaleState), typeof(TopomapSwitchableView),
        new PropertyMetadata(null, OnFieldOrScaleChanged));

    public static readonly DependencyProperty WindowTextProperty = DependencyProperty.Register(
        nameof(WindowText), typeof(string), typeof(TopomapSwitchableView),
        new PropertyMetadata(null, OnFieldOrScaleChanged));

    private Topomap3DWebView? view3D;

    public TopomapSwitchableView()
    {
        InitializeComponent();
        var groupName = $"TopomapDim_{Guid.NewGuid():N}";
        Radio2D.GroupName = groupName;
        Radio3D.GroupName = groupName;
    }

    public TopomapField? Field
    {
        get => (TopomapField?)GetValue(FieldProperty);
        set => SetValue(FieldProperty, value);
    }

    public TopomapColorScaleState? ColorScaleState
    {
        get => (TopomapColorScaleState?)GetValue(ColorScaleStateProperty);
        set => SetValue(ColorScaleStateProperty, value);
    }

    public string? WindowText
    {
        get => (string?)GetValue(WindowTextProperty);
        set => SetValue(WindowTextProperty, value);
    }

    private static void OnFieldOrScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TopomapSwitchableView)d;
        if (control.Radio3D.IsChecked == true && control.view3D is not null)
        {
            control.view3D.ColorScaleState = control.ColorScaleState;
            control.view3D.WindowText = control.WindowText;
            control.view3D.Field = control.Field;
        }
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (Canvas2D is null || Host3D is null || Radio3D is null || DimSwitcherBorder is null) return;
        var is3D = Radio3D.IsChecked == true;
        Canvas2D.Visibility = is3D ? Visibility.Collapsed : Visibility.Visible;
        Host3D.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;

        if (is3D)
        {
            if (view3D is null)
            {
                view3D = new Topomap3DWebView { ShowDimensionSwitcher = true };
                view3D.SwitchTo2DRequested += (_, _) => Radio2D.IsChecked = true;
                view3D.FallbackActivated += (_, _) => DimSwitcherBorder.Visibility = Visibility.Visible;
            }
            if (!ReferenceEquals(Host3D.Content, view3D))
            {
                Host3D.Content = view3D;
            }
            DimSwitcherBorder.Visibility = view3D.IsUsingFallback ? Visibility.Visible : Visibility.Collapsed;
            view3D.ColorScaleState = ColorScaleState;
            view3D.WindowText = WindowText;
            view3D.Field = Field;
        }
        else
        {
            DimSwitcherBorder.Visibility = Visibility.Visible;
        }
    }
}
