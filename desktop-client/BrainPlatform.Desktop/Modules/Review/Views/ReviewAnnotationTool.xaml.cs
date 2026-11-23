using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Modules.Review.Views;

/// <summary>Owns one non-modal annotation window for the current review context.</summary>
public partial class ReviewAnnotationTool : UserControl
{
    private ReviewAnnotationWindow? annotationWindow;

    public ReviewAnnotationTool()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (IsOpen) ShowAnnotationWindow(); };
        Unloaded += (_, _) => IsOpen = false;
        DataContextChanged += (_, _) => IsOpen = false;
    }

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(ReviewAnnotationTool), new PropertyMetadata(false, OnIsOpenChanged));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    private static void OnIsOpenChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var tool = (ReviewAnnotationTool)sender;
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

        var owner = Window.GetWindow(this);
        if (owner is null) return;
        var window = new ReviewAnnotationWindow { Owner = owner, DataContext = DataContext };
        annotationWindow = window;
        var origin = owner.PointToScreen(new Point(0, 0));
        if (PresentationSource.FromVisual(owner)?.CompositionTarget is { } target)
            origin = target.TransformFromDevice.Transform(origin);
        window.Height = Math.Min(window.Height, Math.Max(window.MinHeight, owner.ActualHeight - 160));
        window.Left = origin.X + Math.Max(0, owner.ActualWidth - window.Width - 16);
        window.Top = origin.Y + Math.Max(0, (owner.ActualHeight - window.Height) / 2);
        window.Closed += OnAnnotationWindowClosed;
        window.Show();
    }

    private void OnAnnotationWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not ReviewAnnotationWindow window) return;
        window.Closed -= OnAnnotationWindowClosed;
        if (window.DataContext is RecordingReviewViewModel model)
        {
            model.CancelEventPlacement();
            model.CancelActiveInterval();
            model.IsEventEditorOpen = false;
        }
        window.DataContext = null;
        annotationWindow = null;
        IsOpen = false;
    }
}
