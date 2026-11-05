using System.IO;

namespace BrainPlatform.Desktop.Modules.Review.Export;

public interface IRecordingSignalExportClient
{
    Task DownloadRawSignalExportAsync(string sourceDirectory, string format, Stream destination,
        CancellationToken cancellationToken);
}
