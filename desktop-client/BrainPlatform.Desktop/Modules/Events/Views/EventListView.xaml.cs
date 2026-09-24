using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Modules.Events.Views;

public partial class EventListView : UserControl
{
    public EventListView() => InitializeComponent();

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow) mainWindow.ShowSettingsView();
    }

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowEventDefinitionEditorView();
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EventDefinitionListRow row } &&
            Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowEventDefinitionEditorView(row.Definition);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace ||
            sender is not Button { DataContext: EventDefinitionListRow row } button ||
            !row.CanDelete) return;

        var definition = row.Definition;

        button.IsEnabled = false;
        try
        {
            if (await workspace.EventDefinitions.IsDefinitionReferencedAsync(definition))
            {
                workspace.Notifications.PublishError("该事件已被历史记录引用，只能停用，不能删除。");
                return;
            }

            if (!OperationConfirmationDialog.Confirm(Window.GetWindow(this),
                    OperationConfirmationRequest.DeleteEventDefinition(definition.Name))) return;

            workspace.EventDefinitions.SelectedDefinition = definition;
            await workspace.EventDefinitions.DeleteSelectedAsync();
        }
        catch (Exception exception)
        {
            workspace.Notifications.PublishError(exception.Message);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void OnToggleEnabledClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace ||
            sender is not Button { DataContext: EventDefinitionListRow row } button) return;

        var definition = row.Definition;
        button.IsEnabled = false;
        try { await workspace.EventDefinitions.SetEnabledAsync(definition, !definition.IsEnabled); }
        catch (Exception exception) { workspace.Notifications.PublishError(exception.Message); }
        finally { button.IsEnabled = true; }
    }
}
