using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

/// <summary>
/// Unified 2D / 3D EEG topomap viewer used by Review and Live Acquisition panels.
/// Defaults to 2D TopomapCanvas and lazily activates Topomap3DView when the user switches to 3D.
/// </summary>
public partial class TopomapSwitchableView : UserControl
{
    public static readonly DependencyProperty FieldProperty = DependencyProperty.Register(
        nameof(Field), typeof(TopomapField), typeof(TopomapSwitchableView),
        new PropertyMetadata(null, OnFieldOrScaleChanged));

    public static readonly DependencyProperty ColorScaleStateProperty = DependencyProperty.Register(
        nameof(ColorScaleState), typeof(TopomapColorScaleState), typeof(TopomapSwitchableView),
        new PropertyMetadata(null, OnFieldOrScaleChanged));

    private Topomap3DView? view3D;

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

    private static void OnFieldOrScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TopomapSwitchableView)d;
        if (control.Radio3D.IsChecked == true && control.view3D is not null)
        {
            control.view3D.ColorScaleState = control.ColorScaleState;
            control.view3D.Field = control.Field;
        }
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (Canvas2D is null || Host3D is null || Radio3D is null) return;
        var is3D = Radio3D.IsChecked == true;
        Canvas2D.Visibility = is3D ? Visibility.Collapsed : Visibility.Visible;
        Host3D.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;

        if (is3D)
        {
            view3D ??= new Topomap3DView();
            if (!ReferenceEquals(Host3D.Content, view3D))
            {
                Host3D.Content = view3D;
            }
            view3D.ColorScaleState = ColorScaleState;
            view3D.Field = Field;
        }
    }
}
