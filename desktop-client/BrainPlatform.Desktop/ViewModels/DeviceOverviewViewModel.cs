using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Acquisition.Session;

namespace BrainPlatform.Desktop.ViewModels;

/// <summary>
/// Read-only projection of the shared device session for the application overview.
/// No discovery or selection state is duplicated here.
/// </summary>
public sealed class DeviceOverviewViewModel : ObservableObject, IDisposable
{
    private readonly DeviceSessionManager deviceSession;
    private readonly AcquisitionWorkspaceViewModel acquisition;
    private bool disposed;

    public DeviceOverviewViewModel(
        DeviceSessionManager deviceSession,
        AcquisitionWorkspaceViewModel acquisition)
    {
        this.deviceSession = deviceSession ?? throw new ArgumentNullException(nameof(deviceSession));
        this.acquisition = acquisition ?? throw new ArgumentNullException(nameof(acquisition));
        deviceSession.PropertyChanged += OnDeviceSessionPropertyChanged;
        acquisition.PropertyChanged += OnAcquisitionPropertyChanged;
    }

    public DeviceSessionSnapshot Snapshot => deviceSession.Snapshot;

    public ICommand RefreshDeviceCommand => acquisition.TestConnectionCommand;

    public string StateText => Snapshot.State switch
    {
        DeviceSessionState.NotConfigured => "尚未检测",
        DeviceSessionState.Discovering => "正在检测",
        DeviceSessionState.Disconnected => "未发现设备",
        DeviceSessionState.Connected => "设备已连接",
        DeviceSessionState.Streaming => "数据流运行中",
        DeviceSessionState.Faulted => "设备状态异常",
        _ => "未知状态",
    };

    public Brush StateBrush => Snapshot.State switch
    {
        DeviceSessionState.Connected or DeviceSessionState.Streaming => CreateBrush(31, 136, 96),
        DeviceSessionState.Discovering => CreateBrush(37, 99, 235),
        DeviceSessionState.Faulted => CreateBrush(196, 69, 54),
        _ => CreateBrush(100, 116, 139),
    };

    public Brush StateBackgroundBrush => Snapshot.State switch
    {
        DeviceSessionState.Connected or DeviceSessionState.Streaming => CreateBrush(236, 253, 245),
        DeviceSessionState.Discovering => CreateBrush(239, 246, 255),
        DeviceSessionState.Faulted => CreateBrush(254, 242, 242),
        _ => CreateBrush(248, 250, 252),
    };

    public string DeviceModel => string.IsNullOrWhiteSpace(Snapshot.SelectedDevice?.Model)
        ? "设备未提供"
        : Snapshot.SelectedDevice.Model!;

    public string DeviceId => Snapshot.SelectedDevice?.DeviceId ?? "-";

    public string DeviceInstance => string.IsNullOrWhiteSpace(Snapshot.SelectedDevice?.DeviceInstanceId)
        ? "设备未提供"
        : Snapshot.SelectedDevice.DeviceInstanceId!;

    public string DriverId => Snapshot.DriverId ?? "尚未配置";

    public string CapabilityFingerprint => Snapshot.CapabilityFingerprint ?? "设备连接后生成";

    public string SupportedSamplingRates => Snapshot.SelectedDevice?.SupportedSamplingRatesHz is { Count: > 0 } samplingRates
        ? string.Join(" / ", samplingRates.Order()) + " Hz"
        : "设备连接后读取";

    public string PhysicalInputSummary
    {
        get
        {
            var capabilities = Snapshot.SelectedDevice?.ChannelCapabilities;
            if (capabilities is not { Count: > 0 })
            {
                return "设备连接后读取";
            }

            var referenceInputs = CountRole(capabilities, AcquisitionChannelKind.Reference);
            var bipolarInputs = CountRole(capabilities, AcquisitionChannelKind.Bipolar);
            return $"设备通道 {capabilities.Count} · 参考输入 {referenceInputs} · " +
                   $"双极输入 {bipolarInputs}";
        }
    }

    public string DeviceDetail => Snapshot.Detail;

    public string CheckedAtText => Snapshot.ChangedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string DriverConfigurationText => string.IsNullOrWhiteSpace(acquisition.SdkLibraryPath)
        ? "尚未选择设备 SDK"
        : acquisition.SdkLibraryPath;

    public string OperationMessage => acquisition.OperationMessage;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        deviceSession.PropertyChanged -= OnDeviceSessionPropertyChanged;
        acquisition.PropertyChanged -= OnAcquisitionPropertyChanged;
    }

    private static int CountRole(
        IReadOnlyList<AcquisitionChannelCapability> capabilities,
        AcquisitionChannelKind role) => capabilities.Count(capability => capability.Kind == role);

    private static Brush CreateBrush(byte red, byte green, byte blue) =>
        new SolidColorBrush(Color.FromRgb(red, green, blue));

    private void OnDeviceSessionPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is not nameof(DeviceSessionManager.Snapshot) and not nameof(DeviceSessionManager.SelectedDevice))
        {
            return;
        }

        RaisePropertyChanged(nameof(Snapshot));
        RaisePropertyChanged(nameof(StateText));
        RaisePropertyChanged(nameof(StateBrush));
        RaisePropertyChanged(nameof(StateBackgroundBrush));
        RaisePropertyChanged(nameof(DeviceModel));
        RaisePropertyChanged(nameof(DeviceId));
        RaisePropertyChanged(nameof(DeviceInstance));
        RaisePropertyChanged(nameof(DriverId));
        RaisePropertyChanged(nameof(CapabilityFingerprint));
        RaisePropertyChanged(nameof(SupportedSamplingRates));
        RaisePropertyChanged(nameof(PhysicalInputSummary));
        RaisePropertyChanged(nameof(DeviceDetail));
        RaisePropertyChanged(nameof(CheckedAtText));
    }

    private void OnAcquisitionPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(AcquisitionWorkspaceViewModel.OperationMessage))
        {
            RaisePropertyChanged(nameof(OperationMessage));
        }
        else if (eventArgs.PropertyName == nameof(AcquisitionWorkspaceViewModel.SdkLibraryPath))
        {
            RaisePropertyChanged(nameof(DriverConfigurationText));
        }
    }
}
