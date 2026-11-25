using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Modules.Projects.Views;

public partial class SubjectSelector : UserControl
{
    private CancellationTokenSource lifetime = new();
    public SubjectSelectorViewModel Model { get; } = new();

    public SubjectSelector()
    {
        InitializeComponent();
        ContentPanel.DataContext = Model;
        Unloaded += (_, _) => lifetime.Cancel();
    }

    public async Task InitializeAsync(ProjectWorkspaceViewModel workspace, RecordingSubject initial, ProjectRecordingRow? recording = null)
    {
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = new();
        IsEnabled = true;
        try { await Model.InitializeAsync(workspace, initial, lifetime.Token, recording); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Model.Error = $"读取受试者失败：{exception.Message}"; IsEnabled = false; }
    }

    public Task<RecordingSubject> ResolveAsync() => Model.ResolveAsync(lifetime.Token);

    private void OnEditClick(object sender, RoutedEventArgs e) => Model.BeginEdit();
    private void OnCancelClick(object sender, RoutedEventArgs e) => Model.CancelEdit();
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try { await Model.SaveEditAsync(lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Model.Error = exception.Message; }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (Model.IsBusy || !Model.CanManage || !OperationConfirmationDialog.Confirm(Window.GetWindow(this),
                new("删除受试者", "删除后只解除所有记录的受试者关联，不删除脑电数据。是否继续？", "删除受试者"))) return;
        try { await Model.DeleteAsync(lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Model.Error = exception.Message; }
    }
}
