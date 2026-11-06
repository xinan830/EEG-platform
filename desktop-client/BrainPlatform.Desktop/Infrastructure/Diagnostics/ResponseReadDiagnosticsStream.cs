using System.Diagnostics;
using System.IO;

namespace BrainPlatform.Desktop.Infrastructure.Diagnostics;

/// <summary>Measures streaming reads without buffering; the HTTP response owns the inner stream.</summary>
internal sealed class ResponseReadDiagnosticsStream(Stream inner) : Stream
{
    public long BytesRead { get; private set; }
    public int ReadCalls { get; private set; }
    public double ReadMilliseconds { get; private set; }
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var started = Stopwatch.GetTimestamp();
        ReadCalls++;
        try
        {
            var count = inner.Read(buffer);
            BytesRead += count;
            return count;
        }
        finally { ReadMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
        ReadAsync(buffer.AsMemory(offset, count), token).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        var started = Stopwatch.GetTimestamp();
        ReadCalls++;
        try
        {
            var count = await inner.ReadAsync(buffer, token).ConfigureAwait(false);
            BytesRead += count;
            return count;
        }
        finally { ReadMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    }
}
