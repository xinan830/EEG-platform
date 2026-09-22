using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Views;

public sealed class EegWorkspaceShell : Control
{
    public static readonly DependencyProperty TopBarProperty = DependencyProperty.Register(
        nameof(TopBar),
        typeof(object),
        typeof(EegWorkspaceShell));

    public static readonly DependencyProperty WaveformProperty = DependencyProperty.Register(
        nameof(Waveform),
        typeof(object),
        typeof(EegWorkspaceShell));

    public static readonly DependencyProperty WaveformDataContextProperty = DependencyProperty.Register(
        nameof(WaveformDataContext),
        typeof(object),
        typeof(EegWorkspaceShell));

    public static readonly DependencyProperty BottomBarProperty = DependencyProperty.Register(
        nameof(BottomBar),
        typeof(object),
        typeof(EegWorkspaceShell));

    public static readonly DependencyProperty OverlayProperty = DependencyProperty.Register(
        nameof(Overlay),
        typeof(object),
        typeof(EegWorkspaceShell));

    public static readonly DependencyProperty IsImmersiveWaveformProperty = DependencyProperty.Register(
        nameof(IsImmersiveWaveform),
        typeof(bool),
        typeof(EegWorkspaceShell),
        new PropertyMetadata(false));

    public static readonly DependencyProperty ShowBottomBarProperty = DependencyProperty.Register(
        nameof(ShowBottomBar),
        typeof(bool),
        typeof(EegWorkspaceShell),
        new PropertyMetadata(true));

    public static readonly DependencyProperty IsEdgeToEdgeTopBarProperty = DependencyProperty.Register(
        nameof(IsEdgeToEdgeTopBar),
        typeof(bool),
        typeof(EegWorkspaceShell),
        new PropertyMetadata(false));

    static EegWorkspaceShell()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(EegWorkspaceShell),
            new FrameworkPropertyMetadata(typeof(EegWorkspaceShell)));
    }

    public object? TopBar
    {
        get => GetValue(TopBarProperty);
        set => SetValue(TopBarProperty, value);
    }

    public object? Waveform
    {
        get => GetValue(WaveformProperty);
        set => SetValue(WaveformProperty, value);
    }

    public object? WaveformDataContext
    {
        get => GetValue(WaveformDataContextProperty);
        set => SetValue(WaveformDataContextProperty, value);
    }

    public object? BottomBar
    {
        get => GetValue(BottomBarProperty);
        set => SetValue(BottomBarProperty, value);
    }

    public object? Overlay
    {
        get => GetValue(OverlayProperty);
        set => SetValue(OverlayProperty, value);
    }

    /// <summary>
    /// Uses edge-to-edge waveform rendering for live acquisition without
    /// changing the review workspace's navigation layout.
    /// </summary>
    public bool IsImmersiveWaveform
    {
        get => (bool)GetValue(IsImmersiveWaveformProperty);
        set => SetValue(IsImmersiveWaveformProperty, value);
    }

    public bool ShowBottomBar
    {
        get => (bool)GetValue(ShowBottomBarProperty);
        set => SetValue(ShowBottomBarProperty, value);
    }

    /// <summary>
    /// Removes the outer and waveform spacing around the top bar for the
    /// live acquisition workspace without changing the review workspace.
    /// </summary>
    public bool IsEdgeToEdgeTopBar
    {
        get => (bool)GetValue(IsEdgeToEdgeTopBarProperty);
        set => SetValue(IsEdgeToEdgeTopBarProperty, value);
    }
}
