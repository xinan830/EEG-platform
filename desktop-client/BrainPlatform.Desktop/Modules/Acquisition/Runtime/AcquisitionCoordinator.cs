
namespace BrainPlatform.Desktop.Modules.Acquisition.Runtime;

public sealed class AcquisitionCoordinator : IAsyncDisposable
{
    private readonly IAcquisitionDeviceAdapter deviceAdapter;
    private readonly IAcquisitionRawWriterFactory rawWriterFactory;
    private readonly BoundedAnalysisDispatcher analysisDispatcher;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly SemaphoreSlim batchBoundary = new(1, 1);
    private readonly int ringBufferCapacitySamples;
    private AcquisitionStateSnapshot state = new(
        AcquisitionState.NotConfigured,
        "No acquisition device adapter is configured.",
        null,
        DateTimeOffset.UtcNow);
    private CancellationTokenSource? captureCancellation;
    private Task? captureTask;
    private IAcquisitionStream? stream;
    private IAcquisitionRawWriter? rawWriter;
    private SampleCounterContinuityTracker? continuityTracker;
    private SampleBatchRingBuffer? ringBuffer;
    private Guid? sessionId;
    private Guid? recordingSessionId;
    private AcquisitionStreamRequest? activeRequest;
    private AcquisitionStreamMetadata? streamMetadata;
    private AcquisitionStreamMetadata? recordingMetadata;
    private long recordingFirstSampleCounter = -1;
    private long recordingLastPersistedSampleCounter = -1;
    private AcquisitionFault? lastFault;
    private readonly object pauseGate = new();
    private readonly List<AcquisitionGap> pendingPausedGaps = [];
    private readonly object recordingGapGate = new();
    private readonly List<AcquisitionGap> recordingGaps = [];
    private readonly List<RecordingLifecycleBoundary> lifecycleBoundaries = [];
    private int pauseRequested;
    private bool lifecycleStartedWritten;
    private bool lifecycleResumePending;
    private bool disposed;

    public AcquisitionCoordinator(
        IAcquisitionDeviceAdapter deviceAdapter,
        IAcquisitionRawWriterFactory rawWriterFactory,
        IAcquisitionAnalysisBridge analysisBridge,
        int ringBufferCapacitySamples = 30_000,
        int analysisQueueCapacity = 8)
    {
        this.deviceAdapter = deviceAdapter ?? throw new ArgumentNullException(nameof(deviceAdapter));
        this.rawWriterFactory = rawWriterFactory ?? throw new ArgumentNullException(nameof(rawWriterFactory));
        ringBufferCapacitySamples = ringBufferCapacitySamples > 0
            ? ringBufferCapacitySamples
            : throw new ArgumentOutOfRangeException(nameof(ringBufferCapacitySamples));
        this.ringBufferCapacitySamples = ringBufferCapacitySamples;
        analysisDispatcher = new BoundedAnalysisDispatcher(
            analysisBridge ?? throw new ArgumentNullException(nameof(analysisBridge)),
            analysisQueueCapacity);
        analysisDispatcher.AnalysisFaulted += (_, fault) => AnalysisFaulted?.Invoke(this, fault);
    }

    public event EventHandler<AcquisitionStateSnapshot>? StateChanged;

    public event EventHandler<AcquisitionFault>? AnalysisFaulted;

    public AcquisitionStateSnapshot State => Volatile.Read(ref state);

    public AcquisitionFault? LastFault => lastFault;

    public AcquisitionStreamMetadata? StreamMetadata => Volatile.Read(ref streamMetadata);

    public DateTimeOffset? RecordingStartUtc => Volatile.Read(ref recordingMetadata)?.RecordingStartUtc;

    public long? RecordingFirstSampleCounter
    {
        get
        {
            var value = Volatile.Read(ref recordingFirstSampleCounter);
            return value < 0 ? null : value;
        }
    }

    public IReadOnlyList<AcquisitionBatch> GetDisplaySnapshot() => Volatile.Read(ref ringBuffer)?.Snapshot() ?? [];

    public long? LatestDisplaySampleCounter => Volatile.Read(ref ringBuffer)?.LastSampleCounter;

    public Guid? RecordingSessionId => recordingSessionId;

    public string? RecordingDirectory => rawWriter?.RecordingDirectory;

    public IReadOnlyList<AcquisitionGap> GetRecordingGaps()
    {
        lock (recordingGapGate)
        {
            return recordingGaps.ToArray();
        }
    }

    public IReadOnlyList<RecordingLifecycleBoundary> GetLifecycleBoundaries()
    {
        lock (recordingGapGate) return lifecycleBoundaries.ToArray();
    }

    public async Task<IReadOnlyList<AcquisitionDeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            EnsureState(AcquisitionState.NotConfigured, AcquisitionState.Ready, AcquisitionState.Stopped, AcquisitionState.Faulted);
            SetState(AcquisitionState.Discovering, "Discovering acquisition devices.", null);
            var devices = await deviceAdapter.DiscoverAsync(cancellationToken);
            SetState(
                devices.Count == 0 ? AcquisitionState.NotConfigured : AcquisitionState.Ready,
                devices.Count == 0 ? "No EEG device was discovered." : $"Discovered {devices.Count} device(s).",
                null);
            return devices;
        }
        catch (AcquisitionStateTransitionException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            SetState(AcquisitionState.NotConfigured, "Device discovery was cancelled.", null);
            throw;
        }
        catch (Exception exception)
        {
            SetFault("DEVICE_DISCOVERY_FAILED", exception.Message);
            throw;
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public Task<Guid> StartPreviewAsync(AcquisitionStreamRequest request, CancellationToken cancellationToken) =>
        OpenStreamAsync(request, startRecordingImmediately: false, cancellationToken);

    // Kept for low-level callers that require an immediate record operation.
    public Task<Guid> StartAsync(AcquisitionStreamRequest request, CancellationToken cancellationToken) =>
        OpenStreamAsync(request, startRecordingImmediately: true, cancellationToken);

    private async Task<Guid> OpenStreamAsync(
        AcquisitionStreamRequest request,
        bool startRecordingImmediately,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        request.Validate();
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            EnsureState(AcquisitionState.Ready, AcquisitionState.Stopped, AcquisitionState.NotConfigured, AcquisitionState.Faulted);
            SetState(AcquisitionState.Starting, "Opening EEG stream.", null);

            var openedStream = await deviceAdapter.OpenEegStreamAsync(request, cancellationToken);
            stream = openedStream;
            openedStream.Metadata.Validate();
            if (openedStream.Metadata.SamplingRateHz != request.SamplingRateHz)
            {
                await openedStream.DisposeAsync();
                throw new InvalidOperationException(
                    "The opened stream sampling rate does not match the requested sampling rate.");
            }

            var openedSessionId = Guid.NewGuid();
            IAcquisitionRawWriter? openedWriter = null;
            var publishedMetadata = openedStream.Metadata;
            if (startRecordingImmediately)
            {
                publishedMetadata = openedStream.Metadata with { RecordingStartUtc = DateTimeOffset.UtcNow };
                openedWriter = await rawWriterFactory.CreateAsync(
                    openedSessionId,
                    publishedMetadata,
                    request.Project,
                    request.RecordingDirectory,
                    cancellationToken);
            }

            Volatile.Write(ref rawWriter, openedWriter);
            Volatile.Write(ref recordingMetadata, startRecordingImmediately ? publishedMetadata : null);
            Volatile.Write(ref streamMetadata, openedStream.Metadata);
            sessionId = openedSessionId;
            activeRequest = request;
            Volatile.Write(ref recordingFirstSampleCounter, -1);
            continuityTracker = new SampleCounterContinuityTracker();
            Volatile.Write(ref ringBuffer, new SampleBatchRingBuffer(ringBufferCapacitySamples));
            captureCancellation = new CancellationTokenSource();
            captureTask = Task.Run(() => CaptureLoopAsync(captureCancellation.Token));
            if (startRecordingImmediately)
            {
                recordingSessionId = openedSessionId;
                Volatile.Write(ref streamMetadata, publishedMetadata);
                SetState(AcquisitionState.Recording, "Recording raw EEG data.", openedSessionId);
            }
            else
            {
                SetState(AcquisitionState.Previewing, "EEG stream is open for live preview; raw recording has not started.", openedSessionId);
            }
            return openedSessionId;
        }
        catch (AcquisitionStateTransitionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await CleanupFailedStartAsync();
            SetFault("ACQUISITION_START_FAILED", exception.Message);
            throw;
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async Task<Guid> StartRecordingAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await lifecycle.WaitAsync(cancellationToken);
        IAcquisitionRawWriter? pendingWriter = null;
        try
        {
            EnsureState(AcquisitionState.Previewing);
            var request = activeRequest ?? throw new InvalidOperationException("The preview request is unavailable.");
            var metadata = streamMetadata ?? throw new InvalidOperationException("Stream metadata is unavailable.");
            var streamSessionId = sessionId ?? throw new InvalidOperationException("The stream session is unavailable.");
            var recordingSessionId = Guid.NewGuid();
            var recordingMetadata = metadata with { RecordingStartUtc = DateTimeOffset.UtcNow };
            pendingWriter = await rawWriterFactory.CreateAsync(
                recordingSessionId,
                recordingMetadata,
                request.Project,
                request.RecordingDirectory,
                cancellationToken);

            await batchBoundary.WaitAsync(cancellationToken);
            try
            {
                ClearRecordingGaps();
                Volatile.Write(ref rawWriter, pendingWriter);
                Volatile.Write(ref this.recordingMetadata, recordingMetadata);
                pendingWriter = null;
                this.recordingSessionId = recordingSessionId;
                SetState(AcquisitionState.Recording, "Recording raw EEG data.", streamSessionId);
            }
            finally
            {
                batchBoundary.Release();
            }

            return recordingSessionId;
        }
        catch (AcquisitionStateTransitionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetState(
                AcquisitionState.Previewing,
                $"Raw recording could not start; live preview remains active. {exception.Message}",
                sessionId);
            throw;
        }
        finally
        {
            if (pendingWriter is not null)
            {
                await pendingWriter.DisposeAsync();
            }
            lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        Task? activeCaptureTask = null;
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (state.State is AcquisitionState.NotConfigured or AcquisitionState.Ready or AcquisitionState.Stopped)
            {
                return;
            }

            if (state.State == AcquisitionState.Faulted)
            {
                return;
            }

            SetState(AcquisitionState.Stopping, "Stopping EEG stream.", sessionId);
            captureCancellation?.Cancel();
            activeCaptureTask = captureTask;
        }
        finally
        {
            lifecycle.Release();
        }

        if (activeCaptureTask is not null)
        {
            try
            {
                await activeCaptureTask;
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the normal stop path.
            }
        }

        await CompleteAsync(CancellationToken.None);
    }

    /// <summary>
    /// Stops recording and display persistence while continuing to drain the device stream.
    /// Every skipped sample-counter interval is persisted as an explicit audit gap on resume
    /// or completion; a pause must never fabricate a continuous EEG record.
    /// </summary>
    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            EnsureState(AcquisitionState.Recording);
            Volatile.Write(ref pauseRequested, 1);
            var persistedSample = Volatile.Read(ref recordingLastPersistedSampleCounter);
            if (rawWriter is { } writer && persistedSample >= 0)
            {
                await AppendLifecycleBoundaryAsync(writer, new RecordingLifecycleBoundary(
                    RecordingLifecycleBoundaryKind.Paused, persistedSample, DateTimeOffset.UtcNow), cancellationToken);
            }
            SetState(AcquisitionState.Paused, "EEG recording paused; device stream remains drained.", sessionId);
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            EnsureState(AcquisitionState.Paused);
            Volatile.Write(ref pauseRequested, 0);
            lifecycleResumePending = true;
            SetState(AcquisitionState.Recording, "Recording raw EEG data.", sessionId);
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            await StopAsync(CancellationToken.None);
        }
        finally
        {
            disposed = true;
            await analysisDispatcher.DisposeAsync();
            batchBoundary.Dispose();
            lifecycle.Dispose();
        }
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var activeStream = stream ?? throw new InvalidOperationException("Stream was not initialized.");
            var tracker = continuityTracker ?? throw new InvalidOperationException("Continuity tracker was not initialized.");

            await foreach (var batch in activeStream.ReadBatchesAsync(cancellationToken))
            {
                await ProcessBatchAsync(batch, tracker, cancellationToken);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await CompleteAsync(CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // StopAsync owns completion and raw-file closure.
        }
        catch (AcquisitionContinuityException exception)
        {
            await FaultAsync(new AcquisitionFault(
                "SAMPLE_COUNTER_DISCONTINUITY",
                exception.Message,
                DateTimeOffset.UtcNow));
        }
        catch (Exception exception)
        {
            await FaultAsync(new AcquisitionFault(
                "ACQUISITION_STREAM_FAILED",
                exception.Message,
                DateTimeOffset.UtcNow));
        }
    }

    private async Task ProcessBatchAsync(
        AcquisitionBatch batch,
        SampleCounterContinuityTracker tracker,
        CancellationToken cancellationToken)
    {
        await batchBoundary.WaitAsync(cancellationToken);
        try
        {
            var activeMetadata = streamMetadata ?? throw new InvalidOperationException("Stream metadata was not initialized.");
            if (batch.ChannelCount != activeMetadata.Channels.Count)
            {
                throw new InvalidOperationException(
                    "Received batch channel count does not match the opened stream metadata.");
            }

            var continuity = tracker.Observe(batch);
            var activeWriter = Volatile.Read(ref rawWriter);
            var recordingIsPaused = activeWriter is not null && Volatile.Read(ref pauseRequested) == 1;
            if (recordingIsPaused)
            {
                // Recording pause applies only to immutable raw-file persistence.
                // The device stream, display ring buffer, and live analysis must
                // continue so the operator keeps an uninterrupted preview.
                AccumulatePausedRange(batch);
            }
            else if (activeWriter is not null)
            {
                if (continuity.Gap is not null)
                {
                    await activeWriter.AppendGapAsync(continuity.Gap, cancellationToken);
                    AddRecordingGap(continuity.Gap);
                }

                foreach (var pausedGap in TakePausedGaps())
                {
                    await activeWriter.AppendGapAsync(pausedGap, cancellationToken);
                    AddRecordingGap(pausedGap);
                }

                await activeWriter.AppendBatchAsync(batch, cancellationToken);
                Interlocked.CompareExchange(ref recordingFirstSampleCounter, batch.FirstSampleCounter, -1);
                Volatile.Write(ref recordingLastPersistedSampleCounter, batch.LastSampleCounter);
                if (!lifecycleStartedWritten)
                {
                    lifecycleStartedWritten = true;
                    await AppendLifecycleBoundaryAsync(activeWriter, new RecordingLifecycleBoundary(
                        RecordingLifecycleBoundaryKind.Started, batch.FirstSampleCounter, batch.ReceivedAtUtc), cancellationToken);
                }
                if (lifecycleResumePending)
                {
                    lifecycleResumePending = false;
                    await AppendLifecycleBoundaryAsync(activeWriter, new RecordingLifecycleBoundary(
                        RecordingLifecycleBoundaryKind.Resumed, batch.FirstSampleCounter, batch.ReceivedAtUtc), cancellationToken);
                }
            }

            var displayBuffer = ringBuffer ?? throw new InvalidOperationException("Ring buffer was not initialized.");
            var displayResult = displayBuffer.Append(batch);
            if (displayResult.InputTooLarge && activeWriter is not null)
            {
                await activeWriter.AppendDiagnosticAsync(
                    "DISPLAY_BUFFER_BATCH_TOO_LARGE",
                    "The received batch exceeds display ring-buffer capacity; it remains in raw storage only.",
                    batch.ReceivedAtUtc,
                    cancellationToken);
            }

            var dispatchSessionId = sessionId ?? throw new InvalidOperationException("Stream session was not initialized.");
            var dispatch = analysisDispatcher.TryQueue(new AcquisitionAnalysisBatch(
                dispatchSessionId,
                activeMetadata,
                batch,
                continuity.Gap,
                activeWriter?.RecordingDirectory ?? string.Empty));
            if (!dispatch.Accepted && dispatch.IsNewFailure && activeWriter is not null)
            {
                await activeWriter.AppendDiagnosticAsync(
                    "ANALYSIS_BATCH_DROPPED",
                    dispatch.RejectionReason ?? "Analysis dispatch rejected the batch.",
                    batch.ReceivedAtUtc,
                    cancellationToken);
            }
        }
        finally
        {
            batchBoundary.Release();
        }
    }

    private async Task CompleteAsync(CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (state.State is AcquisitionState.Stopped or AcquisitionState.Faulted)
            {
                return;
            }

            if (rawWriter is not null)
            {
                foreach (var pausedGap in TakePausedGaps())
                {
                    await rawWriter.AppendGapAsync(pausedGap, cancellationToken);
                }

                var lastSample = Volatile.Read(ref recordingLastPersistedSampleCounter);
                if (lastSample >= 0)
                {
                    await AppendLifecycleBoundaryAsync(rawWriter, new RecordingLifecycleBoundary(
                        RecordingLifecycleBoundaryKind.Stopped, lastSample, DateTimeOffset.UtcNow), cancellationToken);
                }

                await rawWriter.CompleteAsync(DateTimeOffset.UtcNow, cancellationToken);
            }

            if (stream is not null)
            {
                await stream.DisposeAsync();
            }

            ResetLiveResources();
            SetState(AcquisitionState.Stopped, "EEG recording stopped.", null);
        }
        finally
        {
            lifecycle.Release();
        }
    }

    private async Task AppendLifecycleBoundaryAsync(
        IAcquisitionRawWriter writer,
        RecordingLifecycleBoundary boundary,
        CancellationToken cancellationToken)
    {
        await writer.AppendLifecycleBoundaryAsync(boundary, cancellationToken);
        lock (recordingGapGate) lifecycleBoundaries.Add(boundary);
    }

    private async Task FaultAsync(AcquisitionFault fault)
    {
        await lifecycle.WaitAsync(CancellationToken.None);
        try
        {
            if (state.State == AcquisitionState.Faulted)
            {
                return;
            }

            if (rawWriter is not null)
            {
                try
                {
                    await rawWriter.AbortAsync(fault, CancellationToken.None);
                }
                catch
                {
                    // The original fault remains the authoritative one.
                }
            }

            if (stream is not null)
            {
                await stream.DisposeAsync();
            }

            ResetLiveResources();
            lastFault = fault;
            SetState(AcquisitionState.Faulted, fault.Detail, null);
        }
        finally
        {
            lifecycle.Release();
        }
    }

    private async Task CleanupFailedStartAsync()
    {
        if (rawWriter is not null)
        {
            await rawWriter.DisposeAsync();
        }

        if (stream is not null)
        {
            await stream.DisposeAsync();
        }

        ResetLiveResources();
    }

    private void ResetLiveResources()
    {
        Volatile.Write(ref pauseRequested, 0);
        lock (pauseGate)
        {
            pendingPausedGaps.Clear();
        }
        ClearRecordingGaps();
        captureCancellation?.Dispose();
        captureCancellation = null;
        captureTask = null;
        stream = null;
        rawWriter = null;
        continuityTracker = null;
        Volatile.Write(ref ringBuffer, null);
        sessionId = null;
        recordingSessionId = null;
        activeRequest = null;
        Volatile.Write(ref recordingFirstSampleCounter, -1);
        Volatile.Write(ref recordingLastPersistedSampleCounter, -1);
        Volatile.Write(ref streamMetadata, null);
        Volatile.Write(ref recordingMetadata, null);
        lifecycleStartedWritten = false;
        lifecycleResumePending = false;
        lock (recordingGapGate) lifecycleBoundaries.Clear();
    }

    private void AccumulatePausedRange(AcquisitionBatch batch)
    {
        lock (pauseGate)
        {
            if (pendingPausedGaps.Count > 0)
            {
                var previous = pendingPausedGaps[^1];
                if (previous.LastMissingSampleCounter != long.MaxValue &&
                    previous.LastMissingSampleCounter + 1 == batch.FirstSampleCounter)
                {
                    pendingPausedGaps[^1] = new AcquisitionGap(
                        previous.FirstMissingSampleCounter,
                        batch.LastSampleCounter,
                        checked(previous.MissingSampleCount + batch.SampleCount),
                        batch.ReceivedAtUtc);
                    return;
                }
            }

            pendingPausedGaps.Add(new AcquisitionGap(
                batch.FirstSampleCounter,
                batch.LastSampleCounter,
                batch.SampleCount,
                batch.ReceivedAtUtc));
        }
    }

    private IReadOnlyList<AcquisitionGap> TakePausedGaps()
    {
        lock (pauseGate)
        {
            if (pendingPausedGaps.Count == 0)
            {
                return [];
            }

            var gaps = pendingPausedGaps.ToArray();
            pendingPausedGaps.Clear();
            return gaps;
        }
    }

    private void AddRecordingGap(AcquisitionGap gap)
    {
        lock (recordingGapGate)
        {
            recordingGaps.Add(gap);
        }
    }

    private void ClearRecordingGaps()
    {
        lock (recordingGapGate)
        {
            recordingGaps.Clear();
        }
    }

    private void EnsureState(params AcquisitionState[] allowed)
    {
        if (!allowed.Contains(state.State))
        {
            throw new AcquisitionStateTransitionException(
                $"Cannot perform this operation while acquisition state is {state.State}.");
        }
    }

    private void SetFault(string code, string detail) =>
        SetFault(new AcquisitionFault(code, detail, DateTimeOffset.UtcNow));

    private void SetFault(AcquisitionFault fault)
    {
        lastFault = fault;
        SetState(AcquisitionState.Faulted, fault.Detail, null);
    }

    private void SetState(AcquisitionState nextState, string detail, Guid? activeSessionId)
    {
        var snapshot = new AcquisitionStateSnapshot(nextState, detail, activeSessionId, DateTimeOffset.UtcNow);
        Volatile.Write(ref state, snapshot);
        StateChanged?.Invoke(this, snapshot);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
