using System.Net;
using System.Net.Http;
using System.IO;

namespace BrainPlatform.Desktop.Modules.Projects.Api;

internal sealed class ProgressStreamContent(Stream source, long length, IProgress<long>? progress)
    : HttpContent
{
    protected override bool TryComputeLength(out long result)
    {
        result = length;
        return true;
    }

    protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) =>
        CopyAsync(target, CancellationToken.None);

    protected override Task SerializeToStreamAsync(Stream target, TransportContext? context, CancellationToken cancellationToken) =>
        CopyAsync(target, cancellationToken);

    private async Task CopyAsync(Stream target, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 1024];
        long sent = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            sent += read;
            progress?.Report(sent);
        }
    }
}
