using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Views;

public partial class ProjectListView : UserControl
{
    public ProjectListView()
    {
        InitializeComponent();
    }

    private DesktopWorkspaceViewModel Workspace => DataContext as DesktopWorkspaceViewModel
        ?? throw new InvalidOperationException("项目页面尚未连接工作区。");

    private async void OnCreateProjectClick(object sender, RoutedEventArgs e)
    {
        var workspace = Workspace;
        var dialog = new ProjectEditorDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            await RunAsync(() => workspace.Projects.SaveAsync(dialog.Draft), workspace.Notifications);
        }
    }

    private async void OnEditProjectClick(object sender, RoutedEventArgs e)
    {
        var workspace = Workspace;
        var project = workspace.Projects.SelectedProject;
        if (project is null)
        {
            return;
        }

        if (workspace.Acquisition.CanStop)
        {
            workspace.Notifications.PublishWarning("采集运行期间不能修改项目设置，请先终止采集。");
            return;
        }

        var dialog = new ProjectEditorDialog(project) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            await RunAsync(async () =>
            {
                var saved = await workspace.Projects.SaveAsync(dialog.Draft, project);
                if (workspace.Acquisition.SelectedProject?.Id == saved.Id)
                {
                    workspace.Acquisition.SelectedProject = saved;
                }
            }, workspace.Notifications);
        }
    }

    private async void OnDeleteProjectClick(object sender, RoutedEventArgs e)
    {
        var workspace = Workspace;
        var project = workspace.Projects.SelectedProject;
        if (project is null)
        {
            return;
        }

        if (workspace.Acquisition.CanStop)
        {
            workspace.Notifications.PublishWarning("采集运行期间不能移除项目，请先终止采集。");
            return;
        }

        var result = MessageBox.Show(
            Window.GetWindow(this),
            $"从平台项目列表移除“{project.Name}”？\n\n磁盘目录和已有原始数据不会删除。",
            "移除项目",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            if (workspace.Acquisition.SelectedProject?.Id == project.Id)
            {
                workspace.Acquisition.SelectedProject = null;
            }
            await RunAsync(workspace.Projects.DeleteSelectedAsync, workspace.Notifications);
        }
    }

    private void OnStartAcquisitionClick(object sender, RoutedEventArgs e)
    {
        var workspace = Workspace;
        var project = workspace.Projects.SelectedProject;
        if (project is null)
        {
            return;
        }

        if (workspace.Acquisition.CanStop)
        {
            workspace.Notifications.PublishWarning("当前已有采集会话，不能切换项目。");
            return;
        }

        workspace.Acquisition.SelectedProject = project;
        workspace.Notifications.PublishSuccess($"已为采集选择项目“{project.Name}”。");
        (Window.GetWindow(this) as MainWindow)?.ShowAcquisitionPreparationView();
    }

    private void OnRefreshRecordingsClick(object sender, RoutedEventArgs e) => Workspace.Projects.RefreshRecordings();

    private static async Task RunAsync(Func<Task> operation, OperationNotificationCenter notifications)
    {
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            notifications.PublishError(exception.Message);
        }
    }
}
