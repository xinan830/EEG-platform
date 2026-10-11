using System.Windows;

namespace BrainPlatform.Desktop.Shared.Dialogs;

/// <summary>Single visual and behavioral entry point for closing an active EEG session.</summary>
public partial class SessionCloseConfirmationDialog : Window
{
    public SessionCloseConfirmationDialog(Window owner, SessionCloseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        InitializeComponent();
        Owner = owner;
        DataContext = request;
    }

    public static bool Confirm(Window owner, SessionCloseRequest request) =>
        OperationConfirmationDialog.Confirm(
            owner,
            new OperationConfirmationRequest(request.TitleText, request.MessageText, request.ConfirmText));

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}

public sealed record SessionCloseRequest(string TitleText, string MessageText, string ConfirmText)
{
    public static SessionCloseRequest Acquisition { get; } = new(
        "关闭实时采集？",
        "关闭后将停止设备数据流。若当前正在记录，系统会先完成并保存已有原始数据及审计信息。",
        "关闭采集");

    public static SessionCloseRequest Review { get; } = new(
        "当前记录存在未保存的标注内容，是否仍要退出回顾页面？",
        "退出后未保存的数据将无法恢复。",
        "确认退出");
}
