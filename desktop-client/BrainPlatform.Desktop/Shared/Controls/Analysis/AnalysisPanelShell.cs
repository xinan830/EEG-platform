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
    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText), typeof(string), typeof(AnalysisPanelShell), new PropertyMetadata(""));
    public static readonly DependencyProperty ApplyCommandProperty = DependencyProperty.Register(
        nameof(ApplyCommand), typeof(ICommand), typeof(AnalysisPanelShell));
    public static readonly DependencyProperty CloseCommandProperty = DependencyProperty.Register(
        nameof(CloseCommand), typeof(ICommand), typeof(AnalysisPanelShell));
    public static readonly RoutedEvent DockToggleRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(DockToggleRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AnalysisPanelShell));

    private Button? dockButton;

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public object? Parameters { get => GetValue(ParametersProperty); set => SetValue(ParametersProperty, value); }
    public string StatusText { get => (string)GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }
    public ICommand? ApplyCommand { get => (ICommand?)GetValue(ApplyCommandProperty); set => SetValue(ApplyCommandProperty, value); }
    public ICommand? CloseCommand { get => (ICommand?)GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }
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

    private void OnDockClick(object sender, RoutedEventArgs e) =>
        RaiseEvent(new RoutedEventArgs(DockToggleRequestedEvent, this));
}
