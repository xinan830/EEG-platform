using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis;

/// <summary>Algorithm-neutral ownership of split layout and reparenting the same panel into a Window.</summary>
public sealed class AnalysisDockHost : UserControl
{
    public static readonly DependencyProperty WaveformProperty = DependencyProperty.Register(
        nameof(Waveform), typeof(FrameworkElement), typeof(AnalysisDockHost), new PropertyMetadata(null, OnContentChanged));
    public static readonly DependencyProperty PanelProperty = DependencyProperty.Register(
        nameof(Panel), typeof(FrameworkElement), typeof(AnalysisDockHost), new PropertyMetadata(null, OnContentChanged));
    public static readonly DependencyProperty IsPanelOpenProperty = DependencyProperty.Register(
        nameof(IsPanelOpen), typeof(bool), typeof(AnalysisDockHost), new PropertyMetadata(false, OnOpenChanged));
    public static readonly DependencyProperty PanelTitleProperty = DependencyProperty.Register(
        nameof(PanelTitle), typeof(string), typeof(AnalysisDockHost), new PropertyMetadata("分析"));

    private readonly Grid workspace = new();
    private readonly ColumnDefinition waveformColumn = new() { Width = new GridLength(1, GridUnitType.Star) };
    private readonly ColumnDefinition splitterColumn = new() { Width = new GridLength(0) };
    private readonly ColumnDefinition panelColumn = new() { Width = new GridLength(0) };
    private readonly GridSplitter splitter = new()
    {
        Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
        ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        ShowsPreview = true, ToolTip = "调整波形与分析区域宽度",
    };
    private GridLength savedWaveformWidth = new(1, GridUnitType.Star);
    private GridLength savedPanelWidth = new(1, GridUnitType.Star);
    private bool isDockedVisible;
    private Window? floatingWindow;

    public AnalysisDockHost()
    {
        workspace.ColumnDefinitions.Add(waveformColumn);
        workspace.ColumnDefinitions.Add(splitterColumn);
        workspace.ColumnDefinitions.Add(panelColumn);
        Grid.SetColumn(splitter, 1);
        splitter.SetResourceReference(BackgroundProperty, "AnalysisDividerBrush");
        Content = workspace;
        DataContextChanged += (_, _) => { if (floatingWindow is not null) floatingWindow.DataContext = DataContext; };
        Unloaded += (_, _) => floatingWindow?.Close();
        UpdateChildren();
    }

    public FrameworkElement? Waveform { get => (FrameworkElement?)GetValue(WaveformProperty); set => SetValue(WaveformProperty, value); }
    public FrameworkElement? Panel { get => (FrameworkElement?)GetValue(PanelProperty); set => SetValue(PanelProperty, value); }
    public bool IsPanelOpen { get => (bool)GetValue(IsPanelOpenProperty); set => SetValue(IsPanelOpenProperty, value); }
    public string PanelTitle { get => (string)GetValue(PanelTitleProperty); set => SetValue(PanelTitleProperty, value); }
    public bool IsFloating => floatingWindow is not null;

    public void ToggleDock()
    {
        if (floatingWindow is not null) { floatingWindow.Close(); return; }
        if (!IsPanelOpen || Panel is null) return;
        workspace.Children.Remove(Panel);
        floatingWindow = new Window
        {
            Title = PanelTitle, Width = 620, Height = 760, MinWidth = 460, MinHeight = 500,
            Owner = Window.GetWindow(this), Content = Panel, DataContext = DataContext,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        floatingWindow.Closed += OnFloatingClosed;
        UpdateLayoutState();
        floatingWindow.Show();
    }

    private void OnFloatingClosed(object? sender, EventArgs e)
    {
        if (floatingWindow is null) return;
        floatingWindow.Closed -= OnFloatingClosed;
        floatingWindow.Content = null;
        floatingWindow = null;
        UpdateChildren();
    }

    private static void OnContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var host = (AnalysisDockHost)sender;
        host.floatingWindow?.Close();
        if (e.Property == PanelProperty)
        {
            if (e.OldValue is FrameworkElement oldPanel) oldPanel.RemoveHandler(AnalysisPanelShell.DockToggleRequestedEvent, new RoutedEventHandler(host.OnDockToggle));
            if (e.NewValue is FrameworkElement newPanel) newPanel.AddHandler(AnalysisPanelShell.DockToggleRequestedEvent, new RoutedEventHandler(host.OnDockToggle));
        }
        host.UpdateChildren();
    }

    private void OnDockToggle(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ToggleDock();
    }

    private static void OnOpenChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var host = (AnalysisDockHost)sender;
        if (!(bool)e.NewValue) host.floatingWindow?.Close();
        host.UpdateLayoutState();
    }

    private void UpdateChildren()
    {
        workspace.Children.Clear();
        if (Waveform is { } waveform) { Grid.SetColumn(waveform, 0); workspace.Children.Add(waveform); }
        workspace.Children.Add(splitter);
        if (Panel is { } panel && floatingWindow is null) { Grid.SetColumn(panel, 2); workspace.Children.Add(panel); }
        UpdateLayoutState();
    }

    private void UpdateLayoutState()
    {
        var visible = IsPanelOpen && floatingWindow is null && Panel is not null;
        if (visible != isDockedVisible)
        {
            if (!visible) { savedWaveformWidth = waveformColumn.Width; savedPanelWidth = panelColumn.Width; }
            isDockedVisible = visible;
            waveformColumn.Width = visible ? savedWaveformWidth : new GridLength(1, GridUnitType.Star);
            splitterColumn.Width = new GridLength(visible ? 6 : 0);
            panelColumn.Width = visible ? savedPanelWidth : new GridLength(0);
        }
        splitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (Panel is not null) Panel.Visibility = IsPanelOpen ? Visibility.Visible : Visibility.Collapsed;
    }
}
