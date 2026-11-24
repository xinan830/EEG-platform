using System.Windows;
using System.Windows.Controls;
using System.IO;
using BrainPlatform.Desktop.Modules.Projects.Api;
using BrainPlatform.Desktop.Modules.Projects.ViewModels;

namespace BrainPlatform.Desktop.Modules.Projects.Views;

internal sealed class RecordingImportDialog : Window
{
    private readonly ProjectWorkspaceViewModel workspace;
    private readonly string filePath;
    private readonly ComboBox subjects = new() { MinWidth = 260, DisplayMemberPath = nameof(BackendResearchSubject.LocalCode) };
    private readonly TextBox newCode = new() { MinWidth = 260, IsEnabled = false };
    private readonly ProgressBar progressBar = new() { Height = 12, Minimum = 0 };
    private readonly TextBlock status = new() { Foreground = System.Windows.Media.Brushes.SlateGray };
    private readonly TextBlock resultSummary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button importButton = new() { Content = "开始导入", MinWidth = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button closeButton = new() { Content = "取消", MinWidth = 78 };
    private CancellationTokenSource? uploadCancellation;
    private long fileLength;
    private bool importCompleted;

    public RecordingImportDialog(ProjectWorkspaceViewModel workspace, string filePath)
    {
        this.workspace = workspace;
        this.filePath = filePath;
        fileLength = new FileInfo(filePath).Length;
        Title = "导入 EDF / BDF";
        Width = 460;
        Height = 330;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Content = BuildContent();
        Loaded += LoadSubjectsAsync;
    }

    private UIElement BuildContent()
    {
        subjects.Items.Add(new BackendResearchSubject("", "", "新建受试者编号…", ""));
        subjects.SelectedIndex = 0;
        subjects.SelectionChanged += (_, _) => newCode.IsEnabled = subjects.SelectedIndex == 0;
        importButton.Click += ImportAsync;
        closeButton.Click += (_, _) =>
        {
            if (uploadCancellation is null) DialogResult = importCompleted;
            else { uploadCancellation.Cancel(); status.Text = "正在取消导入…"; }
        };
        return new StackPanel
        {
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = "文件", FontWeight = FontWeights.SemiBold },
                new TextBlock { Text = Path.GetFileName(filePath), Margin = new Thickness(0, 5, 0, 16), TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = "受试者编号", FontWeight = FontWeights.SemiBold },
                subjects,
                new TextBlock { Text = "新受试者编号", Margin = new Thickness(0, 12, 0, 4) },
                newCode,
                progressBar,
                status,
                resultSummary,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0),
                    Children = { importButton, closeButton },
                },
            },
        };
    }

    private async void LoadSubjectsAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            foreach (var subject in await workspace.ListSubjectsAsync(CancellationToken.None)) subjects.Items.Add(subject);
        }
        catch (Exception exception) { status.Text = $"读取受试者列表失败：{exception.Message}"; }
    }

    private async void ImportAsync(object sender, RoutedEventArgs e)
    {
        if (importCompleted) { DialogResult = true; return; }
        var selected = subjects.SelectedItem as BackendResearchSubject;
        var isNewSubject = selected?.SubjectId == "";
        if (isNewSubject && string.IsNullOrWhiteSpace(newCode.Text))
        {
            status.Text = "请输入受试者编号。";
            return;
        }
        uploadCancellation = new CancellationTokenSource();
        progressBar.Maximum = Math.Max(1, fileLength);
        var progress = new Progress<long>(sent => progressBar.Value = Math.Min(sent, fileLength));
        status.Text = "正在上传并校验文件…";
        importButton.IsEnabled = false;
        try
        {
            var result = await workspace.ImportRecordingAsync(filePath, isNewSubject ? null : selected?.SubjectId,
                isNewSubject ? newCode.Text.Trim() : null, progress, uploadCancellation.Token);
            importCompleted = true;
            var duration = TimeSpan.FromSeconds(result.DurationSeconds);
            resultSummary.Text = $"格式：{result.Extension.TrimStart('.').ToUpperInvariant()}\n" +
                $"采样率：{result.SamplingRateHz:0.##} Hz　时长：{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}　通道：{result.Channels.Count}\n" +
                $"通道顺序：{string.Join("、", result.Channels)}";
            status.Text = "文件已校验并导入。";
            importButton.Content = "完成";
            closeButton.Content = "关闭";
        }
        catch (OperationCanceledException) { status.Text = "导入已取消。"; }
        catch (Exception exception) { status.Text = $"导入失败：{exception.Message}"; }
        finally
        {
            uploadCancellation.Dispose();
            uploadCancellation = null;
            importButton.IsEnabled = true;
        }
    }
}
