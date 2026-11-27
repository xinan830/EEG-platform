using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Settings.Views;

/// <summary>
/// Displays the unified THIRD_PARTY_NOTICES.txt bundle from a single central entry in Settings.
/// </summary>
public sealed partial class ThirdPartyNoticesDialog : Window
{
    private readonly string? resolvedFilePath;

    public ThirdPartyNoticesDialog(string? baseDirectory = null)
    {
        InitializeComponent();
        (resolvedFilePath, var content) = LoadNotices(baseDirectory);
        NoticesTextBox.Text = content;
        SourcePathText.Text = resolvedFilePath is not null
            ? $"随包声明文件：{resolvedFilePath}"
            : "未找到随包 THIRD_PARTY_NOTICES.txt，请联系发行方核对安装包完整性。";
        OpenFolderButton.IsEnabled = resolvedFilePath is not null;
    }

    public static (string? FilePath, string Content) LoadNotices(string? baseDirectory = null)
    {
        var root = string.IsNullOrWhiteSpace(baseDirectory) ? AppContext.BaseDirectory : baseDirectory;
        var candidate = Path.Combine(root, "THIRD_PARTY_NOTICES.txt");
        if (File.Exists(candidate))
        {
            return (candidate, File.ReadAllText(candidate));
        }

        // Fallback when running directly from a dev workspace build without copy.
        var dir = new DirectoryInfo(root);
        while (dir is not null)
        {
            var repoCandidate = Path.Combine(dir.FullName, "THIRD_PARTY_NOTICES.txt");
            if (File.Exists(repoCandidate))
            {
                return (repoCandidate, File.ReadAllText(repoCandidate));
            }
            dir = dir.Parent;
        }

        return (null, "未找到随包声明文件 THIRD_PARTY_NOTICES.txt。");
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (resolvedFilePath is null || !File.Exists(resolvedFilePath))
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{resolvedFilePath}\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell open errors on restricted environments.
        }
    }
}
