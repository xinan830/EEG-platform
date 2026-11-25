using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Projects.Api;
using BrainPlatform.Desktop.Modules.Projects.ViewModels;

namespace BrainPlatform.Desktop.Modules.Projects.Views;

public sealed partial class RecordingImportDialog : Window
{
    private static readonly SolidColorBrush NeutralBrush = new(Color.FromRgb(0x64, 0x74, 0x8B));
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x05, 0x96, 0x69));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xDC, 0x26, 0x26));

    private readonly ProjectWorkspaceViewModel workspace;
    private readonly string filePath;
    private readonly long fileLength;
    private CancellationTokenSource? uploadCancellation;
    private bool importCompleted;

    public RecordingImportDialog(ProjectWorkspaceViewModel workspace, string filePath)
    {
        InitializeComponent();
        this.workspace = workspace;
        this.filePath = filePath;
        var info = new FileInfo(filePath);
        fileLength = info.Exists ? info.Length : 0;

        FormatBadgeText.Text = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext : "EEG";
        FileNameText.Text = Path.GetFileName(filePath);
        FileNameText.ToolTip = filePath;
        var hasSidecar = File.Exists(filePath + ".labels.json");
        FileMetaText.Text = $"{FormatSize(fileLength)}  ·  {(hasSidecar ? "检测到通道/导联侧车文件" : "纯 EDF/BDF 源文件")}";

        Loaded += LoadSubjectsAsync;
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private async void LoadSubjectsAsync(object sender, RoutedEventArgs e)
    {
        ImportButton.IsEnabled = false;
        await SubjectPicker.InitializeAsync(workspace, new());
        ImportButton.IsEnabled = true;
    }

    private void OnCloseOrCancelClick(object sender, RoutedEventArgs e)
    {
        if (uploadCancellation is null)
        {
            DialogResult = importCompleted;
            return;
        }
        uploadCancellation.Cancel();
        SetStatus("正在取消导入…", NeutralBrush);
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (importCompleted)
        {
            DialogResult = true;
            return;
        }

        RecordingSubject selected;
        try { selected = SubjectPicker.Model.Selection; }
        catch (Exception exception) { SetStatus(exception.Message, ErrorBrush); return; }
        uploadCancellation = new CancellationTokenSource();
        var maxBytes = Math.Max(1L, fileLength);
        UploadProgressBar.Maximum = maxBytes;
        UploadProgressBar.Value = 0;
        ProgressPercentText.Text = "0%";
        ProgressPanel.Visibility = Visibility.Visible;
        ResultCard.Visibility = Visibility.Collapsed;
        SetStatus("正在上传并校验脑电文件…", NeutralBrush);
        SetControlsEnabled(false);

        var progress = new Progress<long>(sent =>
        {
            var clamped = Math.Min(sent, maxBytes);
            UploadProgressBar.Value = clamped;
            ProgressPercentText.Text = $"{Math.Clamp((int)Math.Round(clamped * 100d / maxBytes), 0, 100)}%";
        });

        try
        {
            var result = await workspace.ImportRecordingAsync(
                filePath,
                selected.SubjectId,
                selected.SubjectId is null ? selected.Code : null,
                progress,
                uploadCancellation.Token,
                selected.SubjectId is null ? selected.Name : null,
                selected.SubjectId is null ? selected.Sex : null, selected.AgeAtRecordingYears);

            importCompleted = true;
            UploadProgressBar.Value = maxBytes;
            ProgressPercentText.Text = "100%";
            var duration = TimeSpan.FromSeconds(result.DurationSeconds);
            ResultFactsText.Text =
                $"格式：{result.Extension.TrimStart('.').ToUpperInvariant()}   ·   " +
                $"采样率：{result.SamplingRateHz:0.##} Hz   ·   " +
                $"时长：{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}   ·   " +
                $"通道：{result.Channels.Count}";
            ResultChannelsText.Text = $"通道顺序：{string.Join("、", result.Channels)}";
            ResultCard.Visibility = Visibility.Visible;
            ProgressPanel.Visibility = Visibility.Collapsed;
            SetStatus("文件已通过校验并成功导入项目。", SuccessBrush);
            ImportButton.Content = "完成";
            CloseButton.Content = "关闭";
        }
        catch (OperationCanceledException)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            SetStatus("导入已取消。", NeutralBrush);
        }
        catch (Exception exception)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            SetStatus($"导入失败：{exception.Message}", ErrorBrush);
        }
        finally
        {
            uploadCancellation.Dispose();
            uploadCancellation = null;
            SetControlsEnabled(!importCompleted);
            ImportButton.IsEnabled = true;
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        SubjectPicker.IsEnabled = enabled;
        ImportButton.IsEnabled = enabled;
    }

    private void SetStatus(string message, Brush brush)
    {
        StatusText.Text = message;
        StatusText.Foreground = brush;
        StatusText.Visibility = Visibility.Visible;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024d * 1024d):0.##} MB",
        >= 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes} B",
    };
}
