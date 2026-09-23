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
        "关闭数据回溯？",
        "关闭后将停止当前回溯播放并释放已加载的回溯数据。原始记录和派生缓存不会被删除。",
        "关闭回溯");
}
