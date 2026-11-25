using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Modules.Acquisition.Views;

public partial class AcquisitionPreparationView : UserControl
{
    private bool startInProgress;

    public AcquisitionPreparationView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is not DesktopWorkspaceViewModel workspace) return;
            SubjectPicker.IsEnabled = !workspace.Acquisition.CanStop;
            if (!SubjectPicker.IsEnabled) return;
            await SubjectPicker.InitializeAsync(workspace.Projects,
                new(workspace.Acquisition.SubjectCode, workspace.Acquisition.SubjectName, workspace.Acquisition.SubjectId,
                    workspace.Acquisition.SubjectSex, workspace.Acquisition.AgeAtRecordingYears));
        };
        Unloaded += async (_, _) =>
        {
            if (DataContext is not DesktopWorkspaceViewModel workspace) return;
            try { await workspace.Acquisition.CloseImpedanceWindowAsync(); }
            catch (Exception exception) { workspace.Notifications.PublishError(exception.Message); }
        };
    }

    private void OnBackClick(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as IDesktopNavigation)?.ShowProjectDetailView();

    private async void OnImpedanceClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopWorkspaceViewModel workspace) return;
        try { await workspace.Acquisition.ShowImpedanceAsync(Window.GetWindow(this)); }
        catch (Exception exception) { workspace.Notifications.PublishError(exception.Message); }
    }

    private void OnViewProjectClick(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as IDesktopNavigation)?.ShowProjectDetailView();

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (startInProgress || DataContext is not DesktopWorkspaceViewModel workspace)
        {
            return;
        }

        var navigation = Window.GetWindow(this) as IDesktopNavigation;
        startInProgress = true;
        try
        {
            if (workspace.Acquisition.CanStop) throw new InvalidOperationException("采集会话已运行，请先终止采集。");
            var subject = await SubjectPicker.ResolveAsync();
            if (!IsLoaded) return;
            workspace.Acquisition.SubjectCode = subject.Code;
            workspace.Acquisition.SubjectName = subject.Name;
            workspace.Acquisition.SubjectId = subject.SubjectId;
            workspace.Acquisition.SubjectSex = subject.Sex;
            workspace.Acquisition.AgeAtRecordingYears = subject.AgeAtRecordingYears;
            await workspace.Acquisition.StartPreparedPreviewAsync();
            navigation?.ShowAcquisitionView();
        }
        catch (Exception exception)
        {
            workspace.Notifications.PublishError(exception.Message);
        }
        finally
        {
            startInProgress = false;
        }
    }
}
