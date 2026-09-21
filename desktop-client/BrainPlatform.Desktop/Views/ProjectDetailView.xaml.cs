using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Views;

/// <summary>Owns project-detail navigation and actions; it never opens raw EEG itself.</summary>
public partial class ProjectDetailView : UserControl
{
    public ProjectDetailView() => InitializeComponent();

    private DesktopWorkspaceViewModel Workspace => DataContext as DesktopWorkspaceViewModel
        ?? throw new InvalidOperationException("项目详情页尚未连接工作区。");

    private void OnBackToProjectListClick(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.ShowProjectListView();

    private async void OnEditProjectClick(object sender, RoutedEventArgs e)
    {
        var project = Workspace.Projects.SelectedProject;
        if (project is null || Workspace.Acquisition.CanStop)
        {
            Workspace.Notifications.PublishWarning("采集运行期间不能修改项目设置，请先终止采集。");
            return;
        }

        var dialog = new ProjectEditorDialog(project) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var saved = await Workspace.Projects.SaveAsync(dialog.Draft, project);
            if (Workspace.Acquisition.SelectedProject?.Id == saved.Id) Workspace.Acquisition.SelectedProject = saved;
        }
        catch (Exception exception) { Workspace.Notifications.PublishError(exception.Message); }
    }

    private async void OnDeleteProjectClick(object sender, RoutedEventArgs e)
    {
        var project = Workspace.Projects.SelectedProject;
        if (project is null) return;
        if (Workspace.Acquisition.CanStop)
        {
            Workspace.Notifications.PublishWarning("采集运行期间不能移除项目，请先终止采集。");
            return;
        }

        if (!OperationConfirmationDialog.Confirm(
                Window.GetWindow(this),
                OperationConfirmationRequest.RemoveProject(project.Name))) return;
        try
        {
            if (Workspace.Acquisition.SelectedProject?.Id == project.Id) Workspace.Acquisition.SelectedProject = null;
            await Workspace.Projects.DeleteSelectedAsync();
            (Window.GetWindow(this) as MainWindow)?.ShowProjectListView();
        }
        catch (Exception exception) { Workspace.Notifications.PublishError(exception.Message); }
    }

    private void OnStartAcquisitionClick(object sender, RoutedEventArgs e)
    {
        var project = Workspace.Projects.SelectedProject;
        if (project is null) return;
        if (Workspace.Acquisition.CanStop)
        {
            Workspace.Notifications.PublishWarning("当前已有采集会话，不能切换项目。");
            return;
        }
        Workspace.Acquisition.SelectedProject = project;
        (Window.GetWindow(this) as MainWindow)?.ShowAcquisitionPreparationView();
    }

    private void OnOpenProjectDirectoryClick(object sender, RoutedEventArgs e)
    {
        var directory = Workspace.Projects.SelectedProject?.DirectoryPath;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            Workspace.Notifications.PublishWarning("项目目录不存在或当前不可访问。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Workspace.Notifications.PublishError($"无法打开项目目录：{exception.Message}");
        }
    }

    private void OnRecordingPageRequested(object sender, PageRequestedEventArgs e) =>
        Workspace.Projects.GoToRecordingPage(e.Page);

    private async void OnOpenSelectedReviewClick(object sender, RoutedEventArgs e)
    {
        var recording = Workspace.Projects.SelectedRecording;
        if (recording is null || !recording.CanReview)
        {
            Workspace.Notifications.PublishWarning("请先选择一条已完成或已中止的数据记录。");
            return;
        }

        await OpenReviewAsync(recording);
    }

    private async void OnOpenReviewClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ProjectRecordingRow recording || !recording.CanReview) return;
        await OpenReviewAsync(recording);
    }

    private async Task OpenReviewAsync(ProjectRecordingRow recording)
    {
        try
        {
            Workspace.Projects.SelectedRecording = recording;
            if (Window.GetWindow(this) is MainWindow window)
                await window.ShowRecordingReviewViewAsync(recording, Workspace.MontageConfigurations.Profiles);
        }
        catch (Exception exception) { Workspace.Notifications.PublishError(exception.Message); }
    }

    private void OnOpenRecordingMoreMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu })
        {
            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
        }
    }

    private async void OnRenameRecordingClick(object sender, RoutedEventArgs e)
    {
        if (GetRecordingFromMenu(sender) is not { } recording) return;
        var input = new TextBox { Text = recording.Name, MinWidth = 300, Margin = new Thickness(0, 10, 0, 16) };
        var dialog = new Window
        {
            Title = "编辑记录名称", Owner = Window.GetWindow(this), Width = 390, Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        var confirm = new Button { Content = "保存", Width = 78, IsDefault = true };
        confirm.Click += (_, _) => dialog.DialogResult = true;
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Children =
            {
                new TextBlock { Text = "记录名称" }, input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { new Button { Content = "取消", Width = 78, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) }, confirm },
                },
            },
        };
        if (dialog.ShowDialog() != true) return;
        try { await Workspace.Projects.RenameRecordingAsync(recording, input.Text); }
        catch (Exception exception) { Workspace.Notifications.PublishError(exception.Message); }
    }

    private async void OnDeleteRecordingClick(object sender, RoutedEventArgs e)
    {
        if (GetRecordingFromMenu(sender) is not { } recording) return;
        if (!OperationConfirmationDialog.Confirm(
                Window.GetWindow(this),
                OperationConfirmationRequest.DeleteRecording(recording.Name))) return;
        try { await Workspace.Projects.DeleteRecordingAsync(recording); }
        catch (Exception exception) { Workspace.Notifications.PublishError(exception.Message); }
    }

    private static ProjectRecordingRow? GetRecordingFromMenu(object sender) =>
        sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: ProjectRecordingRow recording } } }
            ? recording
            : null;
}
