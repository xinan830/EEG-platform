using System.IO;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Recordings.Access;
using BrainPlatform.Desktop.Modules.Review.Export;
using SharpEDFlib;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed partial class RecordingReviewViewModel
{
    public async Task ExportRecordingAsync(
        string targetPath,
        EDFileType format,
        CancellationToken cancellationToken = default)
    {
        var first = reviewReader.FirstSampleCounter;
        var available = checked(reviewReader.LastSampleCounter - first + 1L);
        if (available <= 0) throw new InvalidDataException("当前录音没有可导出的样本。");

        var channels = reviewReader.Manifest.Channels
            .Where(channel => channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
            .Select(channel => channel.StreamIndex)
            .ToArray();
        var range = new RecordingSampleRange(first, checked(first + available));
        var data = await reviewReader.ReadSamplesAsync(range, channels, cancellationToken);
        await RecordingReviewSignalExporter.ExportAsync(targetPath, format, reviewReader.Manifest,
            recordingStartUtc, first, 0, data, RecordingEvents.ToArray(), cancellationToken);
    }

    public void ReportSignalExportError(Exception exception) => ReportCommandError(exception);
}
