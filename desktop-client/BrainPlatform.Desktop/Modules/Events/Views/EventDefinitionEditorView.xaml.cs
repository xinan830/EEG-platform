using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Modules.Events.Views;

public partial class EventDefinitionEditorView : UserControl
{
    public EventDefinitionEditorView() => InitializeComponent();

    private void OnEditorLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel workspace)
        {
            workspace.EventDefinitions.PropertyChanged += OnDraftPropertyChanged;
            UpdateColorSwatches(workspace.EventDefinitions.DraftColor);
        }
    }

    private void OnEditorUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel workspace)
        {
            workspace.EventDefinitions.PropertyChanged -= OnDraftPropertyChanged;
        }
    }

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EventDefinitionWorkspaceViewModel.DraftColor) &&
            sender is EventDefinitionWorkspaceViewModel viewModel)
        {
            UpdateColorSwatches(viewModel.DraftColor);
        }
    }

    private void UpdateColorSwatches(string color)
    {
        foreach (var button in ColorSwatchPanel.Children.OfType<Button>())
        {
            var selected = string.Equals(button.Tag as string, color, StringComparison.OrdinalIgnoreCase);
            button.BorderBrush = selected ? Brushes.DodgerBlue : Brushes.White;
            button.BorderThickness = selected ? new Thickness(3) : new Thickness(2);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel workspace)
        {
            if (workspace.EventDefinitions.IsSaving) return;
            workspace.EventDefinitions.CancelCommand.Execute(null);
        }

        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowEventListView(refreshDefinitions: false);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace)
        {
            return;
        }

        try
        {
            await workspace.EventDefinitions.SaveDraftAsync();
            if (!workspace.EventDefinitions.IsEditing && Window.GetWindow(this) is MainWindow mainWindow)
            {
                mainWindow.ShowEventListView(refreshDefinitions: false);
            }
        }
        catch (Exception exception)
        {
            workspace.Notifications.PublishError(exception.Message);
        }
    }

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel workspace && sender is FrameworkElement { Tag: string color })
        {
            workspace.EventDefinitions.SetDraftColor(color);
        }
    }

    private void OnPickColorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace) return;

        var color = workspace.EventDefinitions.DraftColorPreviewBrush is SolidColorBrush brush
            ? brush.Color : Colors.RoyalBlue;
        RedColorSlider.Value = color.R;
        GreenColorSlider.Value = color.G;
        BlueColorSlider.Value = color.B;
        UpdatePickerPreview();
        CustomColorPopup.IsOpen = true;
    }

    private void OnPickerValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdatePickerPreview();

    private void UpdatePickerPreview()
    {
        if (PickerColorPreview is null || RedColorSlider is null ||
            GreenColorSlider is null || BlueColorSlider is null) return;

        PickerColorPreview.Background = new SolidColorBrush(Color.FromRgb(
            (byte)Math.Round(RedColorSlider.Value),
            (byte)Math.Round(GreenColorSlider.Value),
            (byte)Math.Round(BlueColorSlider.Value)));
    }

    private void OnApplyPickerColorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel workspace)
        {
            workspace.EventDefinitions.SetDraftColor($"#{(byte)Math.Round(RedColorSlider.Value):X2}" +
                                                     $"{(byte)Math.Round(GreenColorSlider.Value):X2}" +
                                                     $"{(byte)Math.Round(BlueColorSlider.Value):X2}");
        }
        CustomColorPopup.IsOpen = false;
    }

    private void OnCancelPickerColorClick(object sender, RoutedEventArgs e) => CustomColorPopup.IsOpen = false;

    private void OnClearShortcutClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel workspace &&
            !workspace.EventDefinitions.IsDraftSystemDefinition)
        {
            workspace.EventDefinitions.DraftShortcut = string.Empty;
        }
    }

    private void OnShortcutPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (DataContext is not DesktopWorkspaceViewModel workspace ||
            e.IsRepeat || IsModifierKey(key) || key is Key.Tab or Key.Return or Key.Escape or Key.None)
        {
            return;
        }

        workspace.EventDefinitions.DraftShortcut = FormatShortcut(key, Keyboard.Modifiers);
        e.Handled = true;
    }

    private static bool IsModifierKey(Key key) => key is Key.LeftAlt or Key.RightAlt or
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static string FormatShortcut(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join('+', parts);
    }
}
