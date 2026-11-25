using BrainPlatform.Desktop.Modules.Review.Export;
using BrainPlatform.Desktop.Tests.Shared;
using System.IO.Compression;
using System.Xml.Linq;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_or_close_review_cancels_export_and_preserves_destination(bool closeReview)
    {
        var configuration = Configuration();
        var profile = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(profile, AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(profile)], []);
        var client = new BlockingSignalExportClient();
        var root = Directory.CreateTempSubdirectory("review-export-lifetime-").FullName;
        OperationNotificationCenter notifications = null!;
        WpfTestApplication.Run(() => notifications = new OperationNotificationCenter());
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            notifications: notifications, recordingDirectory: root, signalExportClient: client);
        try
        {
            await vm.InitializeAsync();
            var target = Path.Combine(root, "signal.bdf");
            await File.WriteAllTextAsync(target, "original");
            var pending = vm.ExportRecordingAsync(target, "bdf");
            await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(vm.IsSignalExporting);
            Assert.Equal("取消导出", vm.SignalExportButtonText);
            await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ExportRecordingAsync(target, "bdf"));
            if (closeReview) await vm.DisposeAsync();
            else vm.CancelSignalExport();
            await pending.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(client.Token.IsCancellationRequested);
            Assert.False(vm.IsSignalExporting);
            Assert.Equal("导出已取消。", vm.SignalExportStatus);
            Assert.Equal("original", await File.ReadAllTextAsync(target));
            Assert.Empty(Directory.GetDirectories(root));
            WpfTestApplication.Run(() =>
            {
                Assert.Equal(!closeReview, notifications.IsVisible);
                if (!closeReview)
                {
                    Assert.Equal(OperationNotificationKind.Information, notifications.Kind);
                    Assert.Equal("导出已取消。", notifications.Message);
                }
                notifications.Dismiss();
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportCompletionUsesOperationNotificationsAndRestoresButton(bool fail)
    {
        var configuration = Configuration();
        var profile = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(profile, AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(profile)], []);
        var root = Directory.CreateTempSubdirectory("review-export-notice-").FullName;
        OperationNotificationCenter notifications = null!;
        RecordingReviewViewModel vm = null!;
        WpfTestApplication.Run(() =>
        {
            notifications = new OperationNotificationCenter();
            vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
                notifications: notifications, recordingDirectory: root, signalExportClient: new CompletedSignalExportClient(fail));
        });
        try
        {
            Task pending = null!;
            var target = Path.Combine(root, "signal.edf");
            WpfTestApplication.Run(() => pending = vm.ExportRecordingAsync(target, "edf"));
            if (fail)
            {
                var error = await Assert.ThrowsAsync<IOException>(() => pending);
                WpfTestApplication.Run(() => vm.ReportSignalExportError(error));
                Assert.False(File.Exists(target));
            }
            else
            {
                await pending;
                Assert.True(File.Exists(target));
                Assert.True(File.Exists(target + ".labels.json"));
            }
            WpfTestApplication.Run(() =>
            {
                Assert.False(vm.IsSignalExporting);
                Assert.Equal("导出", vm.SignalExportButtonText);
                Assert.True(notifications.IsVisible);
                Assert.Equal(fail ? OperationNotificationKind.Error : OperationNotificationKind.Success, notifications.Kind);
                Assert.Equal(fail ? "导出连接中断" : "整段录音已导出。", notifications.Message);
                notifications.Dismiss();
            });
        }
        finally
        {
            await vm.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExportToolbarDoesNotReserveSpaceForStatusText()
    {
        var view = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RecordingReviewView.xaml"));
        var button = Assert.Single(view.Descendants(), element =>
            (string?)element.Attribute("Content") == "{Binding SignalExportButtonText}");
        Assert.Equal("报告", (string?)button.ElementsAfterSelf().First().Attribute("Content"));
        Assert.DoesNotContain(button.Parent!.Elements(), element =>
            ((string?)element.Attribute("Text"))?.Contains("SignalExportStatus") == true);
    }

    private sealed class CompletedSignalExportClient(bool fail) : IRecordingSignalExportClient
    {
        public Task DownloadRawSignalExportAsync(string directory, string format, Stream destination, CancellationToken token)
        {
            if (fail) throw new IOException("导出连接中断");
            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            using (var writer = new StreamWriter(archive.CreateEntry("recording.edf").Open())) writer.Write("samples");
            using (var writer = new StreamWriter(archive.CreateEntry("recording.edf.labels.json").Open()))
                writer.Write("{\"schema_version\":\"recording-signal-export-v2\"}");
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingSignalExportClient : IRecordingSignalExportClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public async Task DownloadRawSignalExportAsync(string directory, string format, Stream destination, CancellationToken token)
        {
            Token = token;
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
    }
}
