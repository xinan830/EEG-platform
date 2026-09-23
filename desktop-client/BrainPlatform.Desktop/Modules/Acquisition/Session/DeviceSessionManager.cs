using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BrainPlatform.Desktop.Modules.Acquisition.Session;

public enum DeviceSessionState
{
    NotConfigured,
    Discovering,
    Disconnected,
    Connected,
    Streaming,
    Faulted,
}

/// <summary>
/// Immutable hardware snapshot for all desktop pages. Setup preferences such
/// as a selected sampling rate deliberately do not belong here.
/// </summary>
public sealed record DeviceSessionSnapshot(
    DeviceSessionState State,
    DeviceReadiness Readiness,
    string? DriverId,
    AcquisitionDeviceDescriptor? SelectedDevice,
    string? CapabilityFingerprint,
    string Detail,
    DateTimeOffset ChangedAtUtc)
{
    public bool IsReadyForSetup => State == DeviceSessionState.Connected && SelectedDevice is not null;

    public bool IsStreaming => State == DeviceSessionState.Streaming;
}

/// <summary>
/// The sole desktop owner of published device discovery and selection facts.
/// It does not own user setup selections or raw-recording persistence.
/// </summary>
public sealed class DeviceSessionManager : INotifyPropertyChanged, IDisposable
{
    private readonly ConfiguredAcquisitionRuntime runtime;
    private readonly ObservableCollection<AcquisitionDeviceDescriptor> devices = [];
    private readonly ReadOnlyObservableCollection<AcquisitionDeviceDescriptor> readOnlyDevices;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private DeviceSessionSnapshot snapshot;
    private AcquisitionDriverConfiguration? lastConfiguration;
    private bool discoveryInProgress;
    private bool disposed;

    public DeviceSessionManager(ConfiguredAcquisitionRuntime runtime)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        readOnlyDevices = new ReadOnlyObservableCollection<AcquisitionDeviceDescriptor>(devices);
        snapshot = CreateSnapshot(
            DeviceSessionState.NotConfigured,
            runtime.Readiness,
            driverId: null,
            selectedDevice: null,
            "尚未检测采集设备。");
        runtime.StateChanged += OnRuntimeStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyObservableCollection<AcquisitionDeviceDescriptor> Devices => readOnlyDevices;

    internal ConfiguredAcquisitionRuntime Runtime => runtime;

    public AcquisitionDeviceDescriptor? SelectedDevice => Snapshot.SelectedDevice;

    public DeviceSessionSnapshot Snapshot
    {
        get => snapshot;
        private set
        {
            snapshot = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(SelectedDevice));
            RaisePropertyChanged(nameof(IsReadyForSetup));
        }
    }

    public DeviceReadiness Readiness => Snapshot.Readiness;

    public bool IsReadyForSetup => Snapshot.IsReadyForSetup;

    /// <summary>
    /// Re-enumerates the configured idle device without replacing its adapter.
    /// This is intended for background availability checks, never for a live stream.
    /// </summary>
    public async Task RefreshAvailabilityAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (lastConfiguration is null || IsRuntimeStreaming())
        {
            return;
        }

        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (disposed || lastConfiguration is null || IsRuntimeStreaming())
            {
                return;
            }

            // The runtime reports its transient Discovering/Ready states. Availability
            // polling is deliberately silent until a device fact actually changes.
            discoveryInProgress = true;
            var discovered = await Task.Run(() => runtime.DiscoverAsync(cancellationToken), cancellationToken);
            if (disposed)
            {
                return;
            }

            var publishedDevices = PrepareDevices(discovered, lastConfiguration.DriverId);
            if (DeviceSessionDiscoveryComparer.AreEquivalent(devices, publishedDevices))
            {
                return;
            }

            var previousDeviceId = SelectedDevice?.DeviceId;
            ReplaceDevices(publishedDevices);
            PublishDiscoveryResult(lastConfiguration.DriverId, previousDeviceId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (!disposed)
            {
                devices.Clear();
                Snapshot = CreateSnapshot(
                    DeviceSessionState.Faulted,
                    runtime.Readiness,
                    lastConfiguration?.DriverId,
                    selectedDevice: null,
                    $"设备状态检查失败：{exception.Message}");
            }
        }
        finally
        {
            discoveryInProgress = false;
            refreshGate.Release();
        }
    }

    /// <summary>
    /// Reconfirms the selected idle device through the configured SDK before a
    /// stream-open attempt. Successful stream opening remains the final proof
    /// that the device is usable, since it can still disappear after discovery.
    /// </summary>
    public async Task<AcquisitionDeviceDescriptor> ConfirmSelectedDeviceAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await RefreshAvailabilityAsync(cancellationToken);
        return SelectedDevice ?? throw new AcquisitionUnavailableException(
            "开始采集前未确认到所选放大器。请检查设备连接后重新检测。");
    }

    public async Task DiscoverAsync(
        AcquisitionDriverConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ThrowIfDisposed();
        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            var previousDeviceId = SelectedDevice?.DeviceId;
            discoveryInProgress = true;
            Snapshot = CreateSnapshot(
                DeviceSessionState.Discovering,
                runtime.Readiness,
                configuration.DriverId,
                selectedDevice: null,
                "正在读取放大器设备与能力。");
            await runtime.ConfigureAsync(configuration, cancellationToken);
            lastConfiguration = configuration;
            var discovered = await runtime.DiscoverAsync(cancellationToken);
            ReplaceDevices(PrepareDevices(discovered, configuration.DriverId));
            PublishDiscoveryResult(configuration.DriverId, previousDeviceId);
        }
        catch (Exception exception)
        {
            devices.Clear();
            Snapshot = CreateSnapshot(
                DeviceSessionState.Faulted,
                runtime.Readiness,
                configuration.DriverId,
                selectedDevice: null,
                exception.Message);
            throw;
        }
        finally
        {
            discoveryInProgress = false;
            refreshGate.Release();
        }
    }

    /// <summary>Changes the current physical device only to one discovered by this session.</summary>
    public void SelectDevice(AcquisitionDeviceDescriptor? selectedDevice)
    {
        ThrowIfDisposed();
        if (Snapshot.IsStreaming)
        {
            throw new InvalidOperationException("实时数据流运行时不能切换设备。");
        }

        if (selectedDevice is null)
        {
            Snapshot = CreateSnapshot(
                devices.Count == 0 ? DeviceSessionState.Disconnected : DeviceSessionState.Connected,
                runtime.Readiness,
                Snapshot.DriverId,
                selectedDevice: null,
                devices.Count == 0 ? "未发现已连接的放大器。" : "请选择当前设备。");
            return;
        }

        var discovered = devices.FirstOrDefault(device => device.DeviceId == selectedDevice.DeviceId);
        if (discovered is null)
        {
            throw new InvalidOperationException("只能选择当前设备会话已发现的放大器。");
        }

        Snapshot = CreateSnapshot(
            DeviceSessionState.Connected,
            runtime.Readiness,
            Snapshot.DriverId,
            discovered,
            "设备已连接，等待采集准备。");
    }

    /// <summary>
    /// Recreates an idle driver adapter with stream-opening options such as
    /// the user-confirmed channel labels. Discovery facts remain session-owned.
    /// </summary>
    public async Task ConfigureForStreamAsync(
        AcquisitionDriverConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ThrowIfDisposed();
        if (SelectedDevice is null)
        {
            throw new InvalidOperationException("请先检测并选择放大器。");
        }

        await runtime.ConfigureAsync(configuration, cancellationToken);
        lastConfiguration = configuration;
        Snapshot = CreateSnapshot(
            DeviceSessionState.Connected,
            runtime.Readiness,
            configuration.DriverId,
            SelectedDevice,
            "设备已完成采集参数准备，等待打开数据流。");
    }

    public void Invalidate(string detail)
    {
        ThrowIfDisposed();
        devices.Clear();
        lastConfiguration = null;
        Snapshot = CreateSnapshot(
            DeviceSessionState.NotConfigured,
            DeviceReadiness.NotConfigured(),
            driverId: null,
            selectedDevice: null,
            detail);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        runtime.StateChanged -= OnRuntimeStateChanged;
    }

    public static string CreateCapabilityFingerprint(
        string driverId,
        IReadOnlyList<AcquisitionChannelCapability>? capabilities) =>
        AcquisitionDeviceCompatibility.CreateSignature(driverId, capabilities);

    private void OnRuntimeStateChanged(object? sender, AcquisitionStateSnapshot state)
    {
        if (disposed || discoveryInProgress)
        {
            return;
        }

        var nextState = state.State switch
        {
            AcquisitionState.Starting or AcquisitionState.Previewing or AcquisitionState.Recording or AcquisitionState.Paused or AcquisitionState.Stopping =>
                DeviceSessionState.Streaming,
            AcquisitionState.Faulted => DeviceSessionState.Faulted,
            _ when SelectedDevice is not null => DeviceSessionState.Connected,
            _ => DeviceSessionState.Disconnected,
        };
        Snapshot = CreateSnapshot(
            nextState,
            runtime.Readiness,
            Snapshot.DriverId,
            SelectedDevice,
            state.Detail);
    }

    private DeviceSessionSnapshot CreateSnapshot(
        DeviceSessionState state,
        DeviceReadiness readiness,
        string? driverId,
        AcquisitionDeviceDescriptor? selectedDevice,
        string detail) => new(
            state,
            readiness,
            driverId,
            selectedDevice,
            selectedDevice is null || string.IsNullOrWhiteSpace(driverId)
                ? null
                : CreateCapabilityFingerprint(driverId, selectedDevice.ChannelCapabilities),
            detail.Trim(),
            DateTimeOffset.UtcNow);

    private static IReadOnlyList<AcquisitionDeviceDescriptor> PrepareDevices(
        IEnumerable<AcquisitionDeviceDescriptor> discovered,
        string driverId) =>
        discovered
            .Select(device => device with { DriverId = AcquisitionDeviceCompatibility.NormalizeDriverId(driverId) })
            .OrderBy(device => device.DisplayName, StringComparer.Ordinal)
            .ThenBy(device => device.DeviceId, StringComparer.Ordinal)
            .ToArray();

    private void ReplaceDevices(IEnumerable<AcquisitionDeviceDescriptor> discovered)
    {
        devices.Clear();
        foreach (var device in discovered)
        {
            devices.Add(device);
        }

        RaisePropertyChanged(nameof(Devices));
    }

    private void PublishDiscoveryResult(string driverId, string? previousDeviceId)
    {
        var selected = previousDeviceId is null
            ? devices.Count == 1 ? devices[0] : null
            : devices.FirstOrDefault(device => device.DeviceId == previousDeviceId) ??
              (devices.Count == 1 ? devices[0] : null);
        Snapshot = selected is null
            ? CreateSnapshot(
                devices.Count == 0 ? DeviceSessionState.Disconnected : DeviceSessionState.Connected,
                runtime.Readiness,
                driverId,
                selectedDevice: null,
                devices.Count == 0
                    ? "未发现已连接的放大器。"
                    : $"已发现 {devices.Count} 台放大器，请选择当前设备。")
            : CreateSnapshot(
                DeviceSessionState.Connected,
                runtime.Readiness,
                driverId,
                selected,
                "设备已连接，等待采集准备。");
    }

    private bool IsRuntimeStreaming() => runtime.State.State is
        AcquisitionState.Starting or AcquisitionState.Previewing or AcquisitionState.Recording or AcquisitionState.Paused or AcquisitionState.Stopping;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
