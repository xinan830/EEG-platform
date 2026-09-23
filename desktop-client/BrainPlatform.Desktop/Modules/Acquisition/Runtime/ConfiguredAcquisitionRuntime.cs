using System.Net.Http;

namespace BrainPlatform.Desktop.Modules.Acquisition.Runtime;

/// <summary>
/// Application-level owner for an opt-in device adapter and its coordinator.
/// WPF binds to this through a view model; it never owns the native stream.
/// </summary>
public sealed class ConfiguredAcquisitionRuntime : IAsyncDisposable
{
    private const int DisplayHistoryCapacitySamples = 250_000;
    private const int MaximumRetainedDisplayFilterBoundaries = 64;
    private const long FilterProgressTimeoutMilliseconds = 1_000;
    private readonly SemaphoreSlim transition = new(1, 1);
    private readonly AcquisitionDriverRegistry driverRegistry;
    private IAcquisitionDeviceAdapter? adapter;
    private AcquisitionCoordinator? coordinator;
    private readonly HttpLiveFilterBridge? liveFilterBridge;
    private volatile SampleBatchRingBuffer? filteredDisplayBuffer;
    private readonly object displayFilterBoundaryGate = new();
    private readonly List<long> displayFilterBoundaries = [];
    private long lastFilteredProgressTick = -1;
    private long lastFilteredSampleCounter = -1;
    private bool disposed;

    public event EventHandler<AcquisitionStateSnapshot>? StateChanged;

    public event EventHandler<AcquisitionFault>? AnalysisFaulted;

    public DeviceReadiness Readiness => adapter?.GetReadiness() ?? DeviceReadiness.NotConfigured();

    public AcquisitionStateSnapshot State => coordinator?.State ?? new(
        AcquisitionState.NotConfigured,
        "尚未应用采集设备驱动配置。",
        null,
        DateTimeOffset.UtcNow);

    public AcquisitionStreamMetadata? StreamMetadata => coordinator?.StreamMetadata;

    public long? RecordingFirstSampleCounter => coordinator?.RecordingFirstSampleCounter;

    public Guid? RecordingSessionId => coordinator?.RecordingSessionId;

    public string? RecordingDirectory => coordinator?.RecordingDirectory;

    public long? LatestSampleCounter => coordinator?.LatestDisplaySampleCounter;

    public IReadOnlyList<AcquisitionGap> GetRecordingGaps() => coordinator?.GetRecordingGaps() ?? [];

    public IReadOnlyList<AcquisitionDriverDescriptor> AvailableDrivers => driverRegistry.Drivers;

    public ConfiguredAcquisitionRuntime(
        HttpClient? backendHttpClient = null,
        AcquisitionDriverRegistry? driverRegistry = null)
    {
        this.driverRegistry = driverRegistry ?? DesktopAcquisitionDriverCatalog.CreateDefault();
        if (backendHttpClient is not null)
        {
            liveFilterBridge = new HttpLiveFilterBridge(backendHttpClient);
            liveFilterBridge.FilteredBatchAvailable += OnFilteredBatchAvailable;
            liveFilterBridge.FilterConfigurationActivated += OnFilterConfigurationActivated;
            liveFilterBridge.FilterTransitionFailed += OnFilterTransitionFailed;
        }
    }

    public IReadOnlyList<AcquisitionBatch> GetDisplaySnapshot()
    {
        if (liveFilterBridge is null || liveFilterBridge.IsUnavailable)
        {
            return coordinator?.GetDisplaySnapshot() ?? [];
        }

        var filtered = filteredDisplayBuffer?.Snapshot() ?? [];
        if (filtered.Count == 0)
        {
            return coordinator?.GetDisplaySnapshot() ?? [];
        }

        if (coordinator?.LatestDisplaySampleCounter is { } rawLastCounter &&
            StreamMetadata is { } metadata)
        {
            var filteredLastCounter = Volatile.Read(ref lastFilteredSampleCounter);
            var progressTick = Volatile.Read(ref lastFilteredProgressTick);
            var minimumMaterialLag = Math.Max(1, metadata.SamplingRateHz / 4);
            if (filteredLastCounter >= 0 && rawLastCounter - filteredLastCounter >= minimumMaterialLag &&
                progressTick >= 0 && Environment.TickCount64 - progressTick >= FilterProgressTimeoutMilliseconds)
            {
                DisableLiveFilter(new AcquisitionFault(
                    "ANALYSIS_FILTER_STALLED",
                    "实时滤波结果已停止推进，显示已自动切换为未滤波原始数据；设备采集仍继续。",
                    DateTimeOffset.UtcNow));
                return coordinator.GetDisplaySnapshot();
            }
        }

        return filtered;
    }

    public IReadOnlyList<long> GetDisplayFilterBoundaries()
    {
        lock (displayFilterBoundaryGate)
        {
            return displayFilterBoundaries.ToArray();
        }
    }

    public LiveDisplayFilterUpdate ConfigureDisplayFilter(LiveDisplayFilterSettings settings)
    {
        settings.Validate();
        var metadata = StreamMetadata;
        var state = State.State;
        var isLive = metadata is not null &&
            state is (AcquisitionState.Previewing or AcquisitionState.Recording or AcquisitionState.Paused);
        var warmupSeconds = GetWarmupSeconds(settings.LowCutHz);
        var rawBatches = coordinator?.GetDisplaySnapshot() ?? [];
        var bridge = liveFilterBridge ?? throw new AcquisitionUnavailableException(
            "本地科学引擎未配置，不能启用实时滤波。");
        long? effectiveFromRawSampleCounter = null;
        if (isLive)
        {
            effectiveFromRawSampleCounter = rawBatches.Count == 0
                ? 0
                : checked(rawBatches[^1].LastSampleCounter + 1L);
            _ = bridge.ScheduleChange(
                settings,
                effectiveFromRawSampleCounter.Value,
                rawBatches,
                checked(metadata!.SamplingRateHz * warmupSeconds));
        }
        else
        {
            bridge.Configure(settings);
        }

        return new LiveDisplayFilterUpdate(
            isLive,
            warmupSeconds);
    }

    public async Task ConfigureAsync(AcquisitionDriverConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        await transition.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (coordinator?.State.State is AcquisitionState.Starting or AcquisitionState.Previewing or AcquisitionState.Recording or AcquisitionState.Paused or AcquisitionState.Stopping)
            {
                throw new InvalidOperationException("停止当前采集后才能更换设备驱动配置。");
            }

            await DisposeConfiguredRuntimeAsync();
            adapter = driverRegistry.CreateAdapter(configuration);
            filteredDisplayBuffer = liveFilterBridge is null ? null : new SampleBatchRingBuffer(DisplayHistoryCapacitySamples);
            Volatile.Write(ref lastFilteredProgressTick, -1);
            Volatile.Write(ref lastFilteredSampleCounter, -1);
            lock (displayFilterBoundaryGate)
            {
                displayFilterBoundaries.Clear();
            }
            coordinator = new AcquisitionCoordinator(
                adapter,
                new LocalAcquisitionRawWriterFactory(),
                liveFilterBridge is IAcquisitionAnalysisBridge bridge ? bridge : new NullAcquisitionAnalysisBridge(),
                ringBufferCapacitySamples: 250_000);
            coordinator.StateChanged += ForwardStateChanged;
            coordinator.AnalysisFaulted += ForwardAnalysisFault;
        }
        finally
        {
            transition.Release();
        }
    }

    public async Task<IReadOnlyList<AcquisitionDeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken)
    {
        await transition.WaitAsync(cancellationToken);
        try
        {
            return await RequireCoordinator().DiscoverAsync(cancellationToken);
        }
        finally
        {
            transition.Release();
        }
    }

    public async Task<Guid> StartAsync(AcquisitionStreamRequest request, CancellationToken cancellationToken)
    {
        await transition.WaitAsync(cancellationToken);
        try
        {
            return await RequireCoordinator().StartAsync(request, cancellationToken);
        }
        finally
        {
            transition.Release();
        }
    }

    public async Task<Guid> StartPreviewAsync(AcquisitionStreamRequest request, CancellationToken cancellationToken)
    {
        await transition.WaitAsync(cancellationToken);
        try
        {
            return await RequireCoordinator().StartPreviewAsync(request, cancellationToken);
        }
        finally
        {
            transition.Release();
        }
    }

    public async Task<Guid> StartRecordingAsync(CancellationToken cancellationToken)
    {
        await transition.WaitAsync(cancellationToken);
        try
        {
            return await RequireCoordinator().StartRecordingAsync(cancellationToken);
        }
        finally
        {
            transition.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var activeSession = State.SessionId;
        await transition.WaitAsync(cancellationToken);
        try
        {
            await RequireCoordinator().StopAsync(cancellationToken);
            if (activeSession is { } sessionId && liveFilterBridge is not null)
            {
                await liveFilterBridge.CloseAsync(sessionId, cancellationToken);
            }
        }
        finally
        {
            transition.Release();
        }
    }

    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        await transition.WaitAsync(cancellationToken);
        try
        {
            await RequireCoordinator().PauseAsync(cancellationToken);
        }
        finally
        {
            transition.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        await transition.WaitAsync(cancellationToken);
        try
        {
            await RequireCoordinator().ResumeAsync(cancellationToken);
        }
        finally
        {
            transition.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await transition.WaitAsync();
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            await DisposeConfiguredRuntimeAsync();
        }
        finally
        {
            transition.Release();
            transition.Dispose();
        }
    }

    private AcquisitionCoordinator RequireCoordinator() => coordinator
        ?? throw new AcquisitionUnavailableException("请先应用设备驱动配置并测试连接。");

    private async Task DisposeConfiguredRuntimeAsync()
    {
        if (coordinator is not null)
        {
            coordinator.StateChanged -= ForwardStateChanged;
            coordinator.AnalysisFaulted -= ForwardAnalysisFault;
            await coordinator.DisposeAsync();
            coordinator = null;
        }

        if (adapter is not null)
        {
            await adapter.DisposeAsync();
        }
        adapter = null;
        filteredDisplayBuffer = null;
        Volatile.Write(ref lastFilteredProgressTick, -1);
        Volatile.Write(ref lastFilteredSampleCounter, -1);
        lock (displayFilterBoundaryGate)
        {
            displayFilterBoundaries.Clear();
        }
    }

    private void OnFilteredBatchAvailable(object? sender, FilteredDisplayBatch filtered)
    {
        if (liveFilterBridge?.IsUnavailable != true && State.SessionId == filtered.AcquisitionSessionId)
        {
            filteredDisplayBuffer?.Append(filtered.Batch);
            Volatile.Write(ref lastFilteredSampleCounter, filtered.Batch.LastSampleCounter);
            Volatile.Write(ref lastFilteredProgressTick, Environment.TickCount64);
        }
    }

    private void OnFilterConfigurationActivated(object? sender, LiveDisplayFilterActivated activated)
    {
        lock (displayFilterBoundaryGate)
        {
            displayFilterBoundaries.Add(activated.EffectiveFromRawSampleCounter);
            while (displayFilterBoundaries.Count > MaximumRetainedDisplayFilterBoundaries)
            {
                displayFilterBoundaries.RemoveAt(0);
            }
        }
    }

    private void OnFilterTransitionFailed(object? sender, Exception exception)
    {
        AnalysisFaulted?.Invoke(this, new AcquisitionFault(
            "ANALYSIS_FILTER_CHANGE_FAILED",
            $"新的实时滤波配置准备失败，当前滤波仍继续使用：{exception.Message}",
            DateTimeOffset.UtcNow));
    }

    private static int GetWarmupSeconds(double highPassHz)
    {
        var requested = (int)Math.Ceiling(5d / highPassHz);
        return Math.Clamp(requested, 5, 120);
    }

    private void ForwardStateChanged(object? sender, AcquisitionStateSnapshot state)
    {
        if (state.State is AcquisitionState.Faulted or AcquisitionState.Stopped)
        {
            filteredDisplayBuffer?.Clear();
            Volatile.Write(ref lastFilteredProgressTick, -1);
            Volatile.Write(ref lastFilteredSampleCounter, -1);
        }

        StateChanged?.Invoke(this, state);
    }

    private void ForwardAnalysisFault(object? sender, AcquisitionFault fault)
    {
        liveFilterBridge?.MarkUnavailable();
        AnalysisFaulted?.Invoke(this, fault);
    }

    private void DisableLiveFilter(AcquisitionFault fault)
    {
        if (liveFilterBridge?.IsUnavailable == true)
        {
            return;
        }

        liveFilterBridge?.MarkUnavailable();
        AnalysisFaulted?.Invoke(this, fault);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}

public sealed record LiveDisplayFilterUpdate(
    bool AppliedDuringRecording,
    int RequestedWarmupSeconds);
