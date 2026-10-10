using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
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
        var apply = new TestCommand();
        var close = new TestCommand();
        var shell = new AnalysisPanelShell
        {
            DataContext = context, Title = "PSD 分析", Parameters = parameter, Content = chart,
            ApplyCommand = apply, CloseCommand = close,
            StatusText = "参数已改变，请应用并分析。",
        };
        LoadStyles(shell);
        Arrange(shell, width, 640);

        Assert.Same(context, chart.DataContext);
        Assert.Same(context, parameter.DataContext);
        var applyButton = Assert.IsType<Button>(shell.Template.FindName("PART_ApplyButton", shell));
        Assert.Same(apply, applyButton.Command);
        Assert.True(chart.ActualHeight > 420, $"Chart height: {chart.ActualHeight}");
        Assert.True(chart.ActualWidth >= width - 28);
        Assert.True(applyButton.TranslatePoint(new Point(), shell).X + applyButton.ActualWidth <= width);
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
