using System.IO;
using BrainPlatform.Desktop.Modules.Review.Playback;

namespace BrainPlatform.Desktop.Modules.Recordings.Access;

/// <summary>Shares one validated recording source lifetime within a workspace.</summary>
public sealed class RecordingSessionManager : IAsyncDisposable
{
    private const int MaximumConcurrentReadsPerSession = 2;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private int disposed;

    public async Task<RecordingAccessLease> OpenLocalAsync(
        string recordingDirectory,
        Guid expectedSessionId,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (string.IsNullOrWhiteSpace(recordingDirectory) || expectedSessionId == Guid.Empty)
        {
            throw new RecordingAccessException(
                RecordingAccessErrorCodes.IdentityInvalid,
                "打开录音访问会话必须提供有效的目录和录音身份.",
                expectedSessionId);
        }

        var key = CreateKey(recordingDirectory, expectedSessionId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (!entries.TryGetValue(key, out var entry))
            {
                LocalRawRecording recording;
                try
                {
                    recording = await LocalRawRecordingReader.OpenAsync(
                        recordingDirectory, expectedSessionId, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (RecordingReadException exception)
                {
                    var code = exception.Code switch
                    {
                        "RECORDING_IDENTITY_INVALID" or "RECORDING_IDENTITY_MISMATCH" =>
                            RecordingAccessErrorCodes.IdentityInvalid,
                        _ => RecordingAccessErrorCodes.SourceUnavailable,
                    };
                    throw new RecordingAccessException(
                        code,
                        exception.Message,
                        expectedSessionId,
                        innerException: exception);
                }
                if (Volatile.Read(ref disposed) != 0)
                {
                    await recording.DisposeAsync();
                    throw new ObjectDisposedException(nameof(RecordingSessionManager));
                }
                entry = new Entry(key, recording);
                entries.Add(key, entry);
            }

            entry.ReferenceCount++;
            return new RecordingAccessLease(this, entry);
        }
        finally
        {
            gate.Release();
        }
    }

    private static string CreateKey(string directory, Guid sessionId) =>
        $"{Path.GetFullPath(directory)}|{sessionId:N}";

    private async ValueTask ReleaseAsync(Entry entry)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            entry.RequestClose();
            return;
        }

        await gate.WaitAsync();
        try
        {
            if (!entries.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry))
                return;
            if (--current.ReferenceCount > 0) return;
            entries.Remove(entry.Key);
            current.RequestClose();
        }
        finally
        {
            gate.Release();
        }

        await entry.WaitForIdleAsync();
        await entry.Recording.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await gate.WaitAsync();
        Entry[] snapshot;
        try
        {
            snapshot = entries.Values.ToArray();
            entries.Clear();
        }
        finally
        {
            gate.Release();
        }

        foreach (var entry in snapshot)
        {
            entry.RequestClose();
            await entry.WaitForIdleAsync();
            await entry.Recording.DisposeAsync();
        }
    }

    internal sealed class Entry(string key, LocalRawRecording recording)
    {
        private readonly object stateGate = new();
        private readonly TaskCompletionSource idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim readSlots = new(MaximumConcurrentReadsPerSession, MaximumConcurrentReadsPerSession);
        private int activeReads;
        private bool closeRequested;

        public string Key { get; } = key;
        public LocalRawRecording Recording { get; } = recording;
        public int ReferenceCount { get; set; }

        public void EnterRead()
        {
            lock (stateGate)
            {
                if (closeRequested)
                    throw new RecordingAccessException(RecordingAccessErrorCodes.SessionClosed, "录音访问会话已经关闭。", Recording.Manifest.SessionId);
                activeReads++;
            }
        }

        public void ExitRead()
        {
            lock (stateGate)
            {
                if (--activeReads == 0 && closeRequested)
                    idle.TrySetResult();
            }
        }

        public Task WaitForReadSlotAsync(CancellationToken cancellationToken) =>
            readSlots.WaitAsync(cancellationToken);

        public void ReleaseReadSlot() => readSlots.Release();

        public void RequestClose()
        {
            lock (stateGate)
            {
                closeRequested = true;
                if (activeReads == 0)
                    idle.TrySetResult();
            }
        }

        public Task WaitForIdleAsync() => idle.Task;
    }

    public sealed class RecordingAccessLease : IRecordingAccessSession
    {
        private readonly RecordingSessionManager owner;
        private readonly Entry entry;
        private int disposed;

        internal RecordingAccessLease(RecordingSessionManager owner, Entry entry)
        {
            this.owner = owner;
            this.entry = entry;
        }

        private LocalRawRecording Recording => entry.Recording;
        public RecordingAccessMetadata Metadata => Recording.Metadata;
        public LocalRawRecordingManifest Manifest => Recording.Manifest;
        public IReadOnlyList<RecordingLifecycleBoundary> LifecycleBoundaries => Recording.LifecycleBoundaries;
        public double DurationSeconds => Recording.DurationSeconds;
        public long FirstSampleCounter => Recording.FirstSampleCounter;
        public long LastSampleCounter => Recording.LastSampleCounter;

        public async Task<RecordingAccessReadResult> ReadSamplesAsync(
            RecordingSampleRange range,
            IReadOnlyList<int>? channelIndexes,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            entry.EnterRead();
            var acquired = false;
            try
            {
                await entry.WaitForReadSlotAsync(cancellationToken);
                acquired = true;
                return await Recording.ReadSamplesAsync(range, channelIndexes, cancellationToken);
            }
            finally
            {
                if (acquired) entry.ReleaseReadSlot();
                entry.ExitRead();
            }
        }

        public long GetContiguousSegmentOrigin(long sampleCounter)
        {
            ThrowIfDisposed();
            return Recording.GetContiguousSegmentOrigin(sampleCounter);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            await owner.ReleaseAsync(entry);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
                throw new RecordingAccessException(RecordingAccessErrorCodes.SessionClosed, "录音访问会话已经关闭。");
        }
    }
}
