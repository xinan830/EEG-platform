using System.Windows;
using BrainPlatform.Desktop.Modules.Channels.ViewModels;

namespace BrainPlatform.Desktop.Modules.Channels.Views;

/// <summary>Coordinates the user's save, discard, or cancel choice at draft exit boundaries.</summary>
internal static class ChannelDraftLeaveGuard
{
    public static async Task<bool> ConfirmAndCloseAsync(Window owner, DesktopWorkspaceViewModel workspace)
    {
        var configurations = workspace.ChannelConfigurations;
        return await ConfirmAndCloseAsync(
            configurations.IsDraftOpen,
            () => configurations.HasUnsavedChanges,
            () => MessageBox.Show(
                owner,
                "通道配置有未保存的修改。选择“是”保存，选择“否”放弃修改，选择“取消”继续编辑。",
                "未保存的通道配置",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question),
            configurations.SaveDraftAsync,
            configurations.CloseDraft,
            exception => workspace.Notifications.PublishError(exception.Message));
    }

    internal static async Task<bool> ConfirmAndCloseAsync(
        bool isDraftOpen,
        Func<bool> hasUnsavedChanges,
        Func<MessageBoxResult> prompt,
        Func<Task> save,
        Action close,
        Action<Exception> reportSaveFailure)
    {
        if (!isDraftOpen)
        {
            return true;
        }

        if (hasUnsavedChanges())
        {
            var choice = prompt();
            if (choice == MessageBoxResult.Cancel)
            {
                return false;
            }

            if (choice == MessageBoxResult.Yes)
            {
                try
                {
                    await save();
                }
                catch (Exception exception)
                {
                    reportSaveFailure(exception);
                    return false;
                }
            }
        }

        close();
        return true;
    }
}
