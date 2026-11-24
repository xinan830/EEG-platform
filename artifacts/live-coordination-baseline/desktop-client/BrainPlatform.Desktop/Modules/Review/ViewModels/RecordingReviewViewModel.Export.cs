using BrainPlatform.Desktop.Modules.Review.Export;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed partial class RecordingReviewViewModel
{
    private IRecordingSignalExportClient? signalExportClient;
    private CancellationTokenSource? signalExportCancellation;
    private string signalExportStatus = "";
    public string SignalExportStatus
    {
        get => signalExportStatus;
        private set => SetProperty(ref signalExportStatus, value);
    }
    public bool IsSignalExporting => signalExportCancellation is not null;
    public string SignalExportButtonText => IsSignalExporting ? "取消导出" : "导出";
    public void CancelSignalExport() => signalExportCancellation?.Cancel();

    public async Task ExportRecordingAsync(string targetPath, string format,
        CancellationToken cancellationToken = default)
    {
        if (IsSignalExporting) throw new InvalidOperationException("正在导出录音，请等待完成或取消。");
        var client = signalExportClient ?? throw new InvalidOperationException("当前页面未配置后端导出服务。");
        var directory = analysisRecordingDirectory ?? throw new InvalidOperationException("未找到原始录音目录。");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, analysisLifetime.Token);
        signalExportCancellation = cancellation;
        RaisePropertyChanged(nameof(IsSignalExporting));
        RaisePropertyChanged(nameof(SignalExportButtonText));
        SignalExportStatus = "正在导出整段原始录音…";
        try
        {
            await RecordingReviewSignalExporter.ExportAsync(targetPath, format,
                (stream, token) => client.DownloadRawSignalExportAsync(directory, format, stream, token), cancellation.Token);
            SignalExportStatus = "整段录音已导出。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SignalExportStatus = "导出已取消。";
        }
        catch
        {
            SignalExportStatus = "导出失败。";
            throw;
        }
        finally
        {
            signalExportCancellation = null;
            RaisePropertyChanged(nameof(IsSignalExporting));
            RaisePropertyChanged(nameof(SignalExportButtonText));
        }
    }

    public void ReportSignalExportError(Exception exception) => ReportCommandError(exception);
}
