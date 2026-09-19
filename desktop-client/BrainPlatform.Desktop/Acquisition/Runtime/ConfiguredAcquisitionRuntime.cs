using BrainPlatform.Desktop.Acquisition.Analysis;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Acquisition.Drivers;
using BrainPlatform.Desktop.Acquisition.Storage;
using BrainPlatform.Desktop.Domain;
using System.Net.Http;

namespace BrainPlatform.Desktop.Acquisition.Runtime;

/// <summary>
/// Application-level owner for an opt-in device adapter and its coordinator.
/// WPF binds to this through a view model; it never owns the native stream.
/// </summary>
public sealed class ConfiguredAcquisitionRuntime : IAsyncDisposable
{
    private const int DisplayHistoryCapacitySamples = 250_000;
    private const int MaximumRetainedDisplayFilterBoundaries = 64;
    private readonly SemaphoreSlim transition = new(1, 1);
    private readonly AcquisitionDriverRegistry driverRegistry;
    private IAcquisitionDeviceAdapter? adapter;
    private AcquisitionCoordinator? coordinator;
    private readonly HttpLiveFilterBridge? liveFilterBridge;
    private volatile SampleBatchRingBuffer? filteredDisplayBuffer;
    private readonly object displayFilterBoundaryGate = new();
    private readonly List<long> displayFilterBoundaries = [];
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
        }
    }

    public IReadOnlyList<AcquisitionBatch> GetDisplaySnapshot() => liveFilterBridge?.IsUnavailable == true
        ? coordinator?.GetDisplaySnapshot() ?? []
        : filteredDisplayBuffer?.Snapshot() ?? coordinator?.GetDisplaySnapshot() ?? [];

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
        var warmup = isLive
            ? LiveDisplayFilterWarmupFactory.Create(rawBatches, checked(metadata!.SamplingRateHz * warmupSeconds))
            : null;
        var bridge = liveFilterBridge ?? throw new AcquisitionUnavailableException(
            "本地科学引擎未配置，不能启用实时滤波。");
        long? effectiveFromRawSampleCounter = null;
        if (isLive)
        {
            effectiveFromRawSampleCounter = rawBatches.Count == 0
                ? 0
                : checked(rawBatches[^1].LastSampleCounter + 1L);
            var schedule = bridge.ScheduleChange(settings, effectiveFromRawSampleCounter.Value, warmup);
            lock (displayFilterBoundaryGate)
            {
                if (schedule.SupersededPendingBoundary is { } superseded)
                {
                    displayFilterBoundaries.Remove(superseded);
                }

                displayFilterBoundaries.Add(schedule.EffectiveFromRawSampleCounter);
                while (displayFilterBoundaries.Count > MaximumRetainedDisplayFilterBoundaries)
                {
                    displayFilterBoundaries.RemoveAt(0);
                }
            }
        }
        else
        {
            bridge.Configure(settings);
        }

        return new LiveDisplayFilterUpdate(
            isLive,
            warmup?.SampleCount ?? 0,
            effectiveFromRawSampleCounter,
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
        lock (displayFilterBoundaryGate)
        {
            displayFilterBoundaries.Clear();
        }
    }

    private void OnFilteredBatchAvailable(object? sender, FilteredDisplayBatch filtered)
    {
        if (State.SessionId == filtered.AcquisitionSessionId)
        {
            filteredDisplayBuffer?.Append(filtered.Batch);
        }
    }

    private static int GetWarmupSeconds(double highPassHz)
    {
        var requested = (int)Math.Ceiling(5d / highPassHz);
        return Math.Clamp(requested, 5, 120);
    }

    private void ForwardStateChanged(object? sender, AcquisitionStateSnapshot state) => StateChanged?.Invoke(this, state);

    private void ForwardAnalysisFault(object? sender, AcquisitionFault fault) => AnalysisFaulted?.Invoke(this, fault);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}

public sealed record LiveDisplayFilterUpdate(
    bool AppliedDuringRecording,
    int WarmupSampleCount,
    long? EffectiveFromRawSampleCounter,
    int RequestedWarmupSeconds);
