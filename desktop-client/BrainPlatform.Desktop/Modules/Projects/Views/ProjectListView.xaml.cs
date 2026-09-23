using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Projects;
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

    private void OnOpenProjectDetailClick(object sender, RoutedEventArgs e)
    {
        var project = sender switch
        {
            ListBox list => list.SelectedItem as ResearchProject,
            FrameworkElement element => element.DataContext as ResearchProject,
            _ => null,
        };
        if (project is not null) Workspace.Projects.SelectedProject = project;

        if (Workspace.Projects.SelectedProject is not null)
        {
            (Window.GetWindow(this) as MainWindow)?.ShowProjectDetailView();
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

        if (OperationConfirmationDialog.Confirm(
                Window.GetWindow(this),
                OperationConfirmationRequest.RemoveProject(project.Name)))
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

    private void OnProjectPageRequested(object sender, PageRequestedEventArgs e) =>
        Workspace.Projects.GoToProjectPage(e.Page);

    private async void OnOpenReviewClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ProjectRecordingRow recording)
        {
            return;
        }

        var workspace = Workspace;
        workspace.Projects.SelectedRecording = recording;
        if (!recording.CanReview)
        {
            workspace.Notifications.PublishWarning("这条记录尚未完成，暂时不能回溯。");
            return;
        }

        try
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                await mainWindow.ShowRecordingReviewViewAsync(
                    recording,
                    workspace.MontageConfigurations.Profiles);
            }
        }
        catch (Exception exception)
        {
            workspace.Notifications.PublishError(exception.Message);
        }
    }

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
