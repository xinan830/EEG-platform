using System.Windows;

namespace BrainPlatform.Desktop.Shared.Dialogs;

/// <summary>Shared confirmation surface for destructive or session-ending actions.</summary>
public partial class OperationConfirmationDialog : Window
{
    public OperationConfirmationDialog(Window? owner, OperationConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        InitializeComponent();
        Owner = owner;
        DataContext = request;
    }

    public static bool Confirm(Window? owner, OperationConfirmationRequest request) =>
        new OperationConfirmationDialog(owner, request).ShowDialog() == true;

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}

public sealed record OperationConfirmationRequest(string TitleText, string MessageText, string ConfirmText)
{
    public static OperationConfirmationRequest RemoveProject(string projectName) => new(
        "确认移除项目",
        $"确定要从平台项目列表移除“{projectName}”吗？磁盘目录和已有原始数据不会删除。",
        "确认移除");

    public static OperationConfirmationRequest DeleteRecording(string recordingName) => new(
        "确认删除数据",
        $"确定要删除数据记录“{recordingName}”吗？数据会移到当前项目的 .trash 回收目录，可手动恢复。",
        "确认删除");

    public static OperationConfirmationRequest DeleteChannelConfiguration(string name) => new(
        "确认删除通道配置",
        $"确定要删除通道配置“{name}”吗？删除后不能用于新建导联或后续采集。",
        "确认删除");

    public static OperationConfirmationRequest DeleteMontageConfiguration(string name) => new(
        "确认删除导联配置",
        $"确定要删除导联配置“{name}”吗？删除后不能用于新建采集或回溯显示。",
        "确认删除");

    public static OperationConfirmationRequest DeleteEventDefinition(string name) => new(
        "确认删除事件",
        $"确定要删除事件“{name}”吗？已有历史记录引用的事件不能删除。",
        "确认删除");
}
