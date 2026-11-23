using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

namespace BrainPlatform.Desktop.Modules.Acquisition.Views;

/// <summary>Owns one non-modal icon-grid annotation window for the live acquisition session.</summary>
public partial class AcquisitionAnnotationTool : UserControl
{
    private AcquisitionAnnotationWindow? annotationWindow;

    public AcquisitionAnnotationTool()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (IsOpen) ShowAnnotationWindow(); };
        Unloaded += (_, _) => IsOpen = false;
        DataContextChanged += (_, _) => IsOpen = false;
    }

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(AcquisitionAnnotationTool), new PropertyMetadata(false, OnIsOpenChanged));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    private static void OnIsOpenChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var tool = (AcquisitionAnnotationTool)sender;
        if (e.NewValue is true) tool.ShowAnnotationWindow();
        else tool.annotationWindow?.Close();
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (IsOpen) ShowAnnotationWindow();
        else IsOpen = true;
    }

    private void ShowAnnotationWindow()
    {
        if (!IsLoaded) return;
        if (annotationWindow is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        if (DataContext is not DesktopWorkspaceViewModel workspace) return;
        var owner = Window.GetWindow(this);
        if (owner is null) return;

        _ = RefreshDefinitionsSafeAsync(workspace.Acquisition);

        var window = new AcquisitionAnnotationWindow(workspace.Notifications)
        {
            Owner = owner,
            DataContext = workspace.Acquisition,
        };
        annotationWindow = window;
        var origin = owner.PointToScreen(new Point(0, 0));
        if (PresentationSource.FromVisual(owner)?.CompositionTarget is { } target)
        {
            origin = target.TransformFromDevice.Transform(origin);
        }

        window.Height = Math.Min(window.Height, Math.Max(window.MinHeight, owner.ActualHeight - 160));
        window.Left = origin.X + Math.Max(0, owner.ActualWidth - window.Width - 16);
        window.Top = origin.Y + Math.Max(0, (owner.ActualHeight - window.Height) / 2);
        window.Closed += OnAnnotationWindowClosed;
        window.Show();
    }

    private async Task RefreshDefinitionsSafeAsync(AcquisitionWorkspaceViewModel acquisition)
    {
        try
        {
            await acquisition.RefreshEventDefinitionsAsync();
            annotationWindow?.EventGrid.RefreshEmptyState();
        }
        catch
        {
            // Keep existing tiles if refresh fails transiently.
        }
    }

    private void OnAnnotationWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not AcquisitionAnnotationWindow window) return;
        window.Closed -= OnAnnotationWindowClosed;
        window.DataContext = null;
        annotationWindow = null;
        IsOpen = false;
    }
}
