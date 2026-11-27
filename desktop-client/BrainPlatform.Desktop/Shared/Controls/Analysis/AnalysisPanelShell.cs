using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis;

/// <summary>Presentation slots only. Commands, parameters and charts belong to the caller.</summary>
public sealed class AnalysisPanelShell : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(AnalysisPanelShell), new PropertyMetadata("分析"));
    public static readonly DependencyProperty ParametersProperty = DependencyProperty.Register(
        nameof(Parameters), typeof(object), typeof(AnalysisPanelShell));
    public static readonly DependencyProperty HeaderContentProperty = DependencyProperty.Register(
        nameof(HeaderContent), typeof(object), typeof(AnalysisPanelShell));
    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText), typeof(string), typeof(AnalysisPanelShell), new PropertyMetadata(""));
    public static readonly DependencyProperty ApplyCommandProperty = DependencyProperty.Register(
        nameof(ApplyCommand), typeof(ICommand), typeof(AnalysisPanelShell));
    public static readonly DependencyProperty CloseCommandProperty = DependencyProperty.Register(
        nameof(CloseCommand), typeof(ICommand), typeof(AnalysisPanelShell));
    public static readonly DependencyProperty DockCommandProperty = DependencyProperty.Register(
        nameof(DockCommand), typeof(ICommand), typeof(AnalysisPanelShell));
    public static readonly DependencyProperty DockToolTipProperty = DependencyProperty.Register(
        nameof(DockToolTip), typeof(string), typeof(AnalysisPanelShell), new PropertyMetadata("停靠 / 分离"));
    public static readonly DependencyProperty MaximizeCommandProperty = DependencyProperty.Register(
        nameof(MaximizeCommand), typeof(ICommand), typeof(AnalysisPanelShell));
    public static readonly DependencyProperty MaximizeToolTipProperty = DependencyProperty.Register(
        nameof(MaximizeToolTip), typeof(string), typeof(AnalysisPanelShell), new PropertyMetadata("放大 / 还原"));
    public static readonly DependencyProperty IsMaximizeVisibleProperty = DependencyProperty.Register(
        nameof(IsMaximizeVisible), typeof(bool), typeof(AnalysisPanelShell), new PropertyMetadata(false));
    public static readonly DependencyProperty IsMaximizedProperty = DependencyProperty.Register(
        nameof(IsMaximized), typeof(bool), typeof(AnalysisPanelShell), new PropertyMetadata(false));
    public static readonly RoutedEvent DockToggleRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(DockToggleRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AnalysisPanelShell));

    private Button? dockButton;

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public object? Parameters { get => GetValue(ParametersProperty); set => SetValue(ParametersProperty, value); }
    public object? HeaderContent { get => GetValue(HeaderContentProperty); set => SetValue(HeaderContentProperty, value); }
    public string StatusText { get => (string)GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }
    public ICommand? ApplyCommand { get => (ICommand?)GetValue(ApplyCommandProperty); set => SetValue(ApplyCommandProperty, value); }
    public ICommand? CloseCommand { get => (ICommand?)GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }
    public ICommand? DockCommand { get => (ICommand?)GetValue(DockCommandProperty); set => SetValue(DockCommandProperty, value); }
    public string DockToolTip { get => (string)GetValue(DockToolTipProperty); set => SetValue(DockToolTipProperty, value); }
    public ICommand? MaximizeCommand { get => (ICommand?)GetValue(MaximizeCommandProperty); set => SetValue(MaximizeCommandProperty, value); }
    public string MaximizeToolTip { get => (string)GetValue(MaximizeToolTipProperty); set => SetValue(MaximizeToolTipProperty, value); }
    public bool IsMaximizeVisible { get => (bool)GetValue(IsMaximizeVisibleProperty); set => SetValue(IsMaximizeVisibleProperty, value); }
    public bool IsMaximized { get => (bool)GetValue(IsMaximizedProperty); set => SetValue(IsMaximizedProperty, value); }

    public event RoutedEventHandler DockToggleRequested
    {
        add => AddHandler(DockToggleRequestedEvent, value);
        remove => RemoveHandler(DockToggleRequestedEvent, value);
    }

    public override void OnApplyTemplate()
    {
        if (dockButton is not null) dockButton.Click -= OnDockClick;
        base.OnApplyTemplate();
        dockButton = GetTemplateChild("PART_DockButton") as Button;
        if (dockButton is not null) dockButton.Click += OnDockClick;
    }

    private void OnDockClick(object sender, RoutedEventArgs e)
    {
        if (DockCommand is { } command)
        {
            if (command.CanExecute(CommandParameterFallback())) command.Execute(CommandParameterFallback());
            return;
        }
        RaiseEvent(new RoutedEventArgs(DockToggleRequestedEvent, this));
    }

    private object? CommandParameterFallback() => DataContext;
}
