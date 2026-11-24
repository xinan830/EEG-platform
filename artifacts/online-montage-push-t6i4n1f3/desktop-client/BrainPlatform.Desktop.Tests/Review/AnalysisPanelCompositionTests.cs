using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using BrainPlatform.Desktop.Shared.Controls.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class AnalysisPanelCompositionTests
{
    [Theory]
    [InlineData(320)]
    [InlineData(620)]
    public void Shared_shell_retains_context_commands_and_large_chart_slot(double width) => OnSta(() =>
    {
        var context = new object();
        var chart = new Border();
        var parameter = new TextBlock { Text = "原始通道", Height = 50 };
        var close = new TestCommand();
        var shell = new AnalysisPanelShell
        {
            DataContext = context, Title = "RBP 分析", Parameters = parameter, Content = chart,
            CloseCommand = close,
            StatusText = "参数已改变，等待自动分析。",
        };
        LoadStyles(shell);
        Arrange(shell, width, 640);

        Assert.Same(context, chart.DataContext);
        Assert.Same(context, parameter.DataContext);
        var applyButton = Assert.IsType<Button>(shell.Template.FindName("PART_ApplyButton", shell));
        Assert.Equal(Visibility.Collapsed, applyButton.Visibility);
        Assert.True(chart.ActualHeight > 420, $"Chart height: {chart.ActualHeight}");
        Assert.True(chart.ActualWidth >= width - 28);
        var toggles = 0;
        shell.DockToggleRequested += (_, _) => toggles++;
        var dock = Assert.IsType<Button>(shell.Template.FindName("PART_DockButton", shell));
        dock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, toggles);
        shell.Template = shell.Template;
        shell.ApplyTemplate();
        dock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, toggles);
    });

    [Fact]
    public void Dock_host_collapses_and_restores_split_without_replacing_panel_or_context() => OnSta(() =>
    {
        var context = new object();
        var panel = new Border();
        var waveform = new Border();
        var host = new AnalysisDockHost { DataContext = context, Waveform = waveform, Panel = panel, IsPanelOpen = true };
        Arrange(host, 1000, 600);
        var initialWidth = panel.ActualWidth;
        Assert.InRange(initialWidth, 490, 500);
        Assert.Same(context, panel.DataContext);
        host.IsPanelOpen = false;
        Arrange(host, 1000, 600);
        Assert.Equal(Visibility.Collapsed, panel.Visibility);
        Assert.Equal(1000, waveform.ActualWidth);
        host.IsPanelOpen = true;
        Arrange(host, 1000, 600);
        Assert.Equal(initialWidth, panel.ActualWidth);
        Assert.Same(panel, host.Panel);
        Assert.Same(context, panel.DataContext);
    });

    [Fact]
    public void Dock_host_places_tnk_below_waveform_and_restores_both_split_directions() => OnSta(() =>
    {
        var panel = new Border();
        var waveform = new Border();
        var host = new AnalysisDockHost
        {
            Waveform = waveform,
            Panel = panel,
            IsPanelBelowWaveform = true,
            IsPanelOpen = true,
        };

        Arrange(host, 800, 700);
        Assert.Equal(800, waveform.ActualWidth);
        Assert.Equal(800, panel.ActualWidth);
        Assert.InRange(waveform.ActualHeight, 340, 350);
        Assert.InRange(panel.ActualHeight, 340, 350);

        host.IsPanelBelowWaveform = false;
        Arrange(host, 800, 700);
        Assert.InRange(waveform.ActualWidth, 390, 400);
        Assert.InRange(panel.ActualWidth, 390, 400);

        host.IsPanelBelowWaveform = true;
        Arrange(host, 800, 700);
        Assert.InRange(waveform.ActualHeight, 340, 350);
        Assert.InRange(panel.ActualHeight, 340, 350);
    });

    [Fact]
    public void Tnk_review_panel_uses_compact_single_row_tabs_above_chart_and_summary() => OnSta(() =>
    {
        var panel = new BrainPlatform.Desktop.Modules.Review.Views.ReviewTnkAnalysisPanel();
        var shell = Assert.IsType<AnalysisPanelShell>(panel.Content);
        var tabs = Assert.IsType<ListBox>(panel.FindName("MetricTabs"));
        var channels = Assert.IsType<ItemsControl>(panel.FindName("ChannelSelectors"));
        var chart = Assert.IsType<ScrollViewer>(panel.FindName("MetricContent"));
        var summary = Assert.IsType<ScrollViewer>(panel.FindName("SummarySidebar"));
        var recentMetrics = Assert.IsType<StackPanel>(panel.FindName("RecentMetricsLayout"));
        var corticalValue = Assert.IsType<TextBlock>(panel.FindName("CorticalValueText"));
        var tabColumns = Assert.IsType<UniformGrid>(tabs.ItemsPanel.LoadContent());

        Assert.Equal(new Thickness(6, 4, 6, 4), shell.Padding);
        Assert.Equal("", shell.StatusText);
        Assert.Same(channels, shell.HeaderContent);
        Assert.Equal(0, Grid.GetRow(tabs));
        Assert.Equal(0, Grid.GetColumn(tabs));
        Assert.Equal(1, Grid.GetRow(chart));
        Assert.Equal(0, Grid.GetRow(summary));
        Assert.Equal(1, Grid.GetColumn(summary));
        Assert.Equal(2, Grid.GetRowSpan(summary));
        Assert.Equal(9, tabColumns.Columns);
        Assert.Equal(1, tabColumns.Rows);
        Assert.Equal(3, recentMetrics.Children.Count);
        Assert.Equal(TextTrimming.CharacterEllipsis, corticalValue.TextTrimming);

        Arrange(panel, 1200, 600);
        shell.ApplyTemplate();
        var header = Assert.IsType<ContentPresenter>(shell.Template.FindName("PART_HeaderContent", shell));
        var dock = Assert.IsType<Button>(shell.Template.FindName("PART_DockButton", shell));
        Assert.Equal(Grid.GetRow(dock), Grid.GetRow(header));
    });

    [Fact]
    public void Floating_panel_returns_to_same_host_and_preserves_open_binding() => OnSta(() =>
    {
        var state = new OpenState();
        var panel = new Border();
        var host = new AnalysisDockHost { DataContext = state, Waveform = new Border(), Panel = panel };
        host.SetBinding(AnalysisDockHost.IsPanelOpenProperty, new Binding(nameof(OpenState.IsOpen)));
        Arrange(host, 1000, 600);
        try
        {
            host.ToggleDock();
            Assert.True(host.IsFloating);
            Assert.Same(panel, host.Panel);
            Assert.Same(state, panel.DataContext);
            host.ToggleDock();
            Assert.False(host.IsFloating);
            Assert.Same(state, panel.DataContext);
            Assert.NotNull(BindingOperations.GetBinding(host, AnalysisDockHost.IsPanelOpenProperty));
            host.ToggleDock();
            host.IsPanelOpen = false;
            Assert.False(host.IsFloating);
            Assert.Equal(Visibility.Collapsed, panel.Visibility);
        }
        finally { if (host.IsFloating) host.ToggleDock(); }
    });

    private static void LoadStyles(FrameworkElement element)
    {
        foreach (var name in new[] { "ControlStyles", "WorkspaceStyles", "AnalysisStyles" })
            element.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/BrainPlatform.Desktop;component/Shared/Styles/{name}.xaml", UriKind.RelativeOrAbsolute),
            });
    }

    private static void Arrange(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class OpenState { public bool IsOpen => true; }
    private sealed class TestCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }
}
