using BrainPlatform.Desktop.Modules.Review.Export;

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
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            recordingDirectory: root, signalExportClient: client);
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
        }
        finally { Directory.Delete(root, recursive: true); }
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
