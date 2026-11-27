using System.Collections.ObjectModel;
using System.Globalization;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

/// <summary>
/// Owns device-derived sampling-rate and reference/bipolar input-range selection,
/// manual range editing state, persisted setup defaults, and capability display summaries.
/// Reads device session and runtime state without duplicating either owner.
/// </summary>
internal sealed class AcquisitionDeviceSetupController : ObservableObject
{
    private readonly DeviceSessionManager deviceSession;
    private readonly ConfiguredAcquisitionRuntime runtime;
    private AcquisitionDeviceDescriptor? appliedSetupDevice;
    private IReadOnlyList<double> availableBipolarRanges = [];
    private int? savedSamplingRateHz;
    private string savedReferenceRangeText;
    private string savedBipolarRangeText;
    private int? selectedSamplingRateHz;
    private double? selectedReferenceRangeVolts;
    private double? selectedBipolarRangeVolts;
    private double? automaticReferenceRangeVolts;
    private double? automaticBipolarRangeVolts;
    private bool isEditingInputRanges;
    private bool isManualInputRangeSelection;

    public AcquisitionDeviceSetupController(
        DeviceSessionManager deviceSession,
        ConfiguredAcquisitionRuntime runtime,
        int? savedSamplingRateHz,
        string savedReferenceRangeText,
        string savedBipolarRangeText)
    {
        this.deviceSession = deviceSession;
        this.runtime = runtime;
        this.savedSamplingRateHz = savedSamplingRateHz;
        this.savedReferenceRangeText = savedReferenceRangeText;
        this.savedBipolarRangeText = savedBipolarRangeText;
    }

    public ObservableCollection<int> SamplingRatesHz { get; } = [];

    public ObservableCollection<double> ReferenceRangesVolts { get; } = [];

    public ObservableCollection<double> BipolarRangesVolts { get; } = [];

    public int? SavedSamplingRateHz => savedSamplingRateHz;

    public string SavedReferenceRangeText => savedReferenceRangeText;

    public string SavedBipolarRangeText => savedBipolarRangeText;

    public int? SelectedSamplingRateHz
    {
        get => selectedSamplingRateHz;
        set => SetProperty(ref selectedSamplingRateHz, value);
    }

    public double? SelectedReferenceRangeVolts
    {
        get => selectedReferenceRangeVolts;
        set
        {
            if (SetProperty(ref selectedReferenceRangeVolts, value))
            {
                RefreshBipolarRangeChoices();
                RaiseInputRangePropertiesChanged();
            }
        }
    }

    public double? SelectedBipolarRangeVolts
    {
        get => selectedBipolarRangeVolts;
        set
        {
            if (SetProperty(ref selectedBipolarRangeVolts, value))
            {
                RaiseInputRangePropertiesChanged();
            }
        }
    }

    public bool IsEditingInputRanges
    {
        get => isEditingInputRanges;
        private set
        {
            if (SetProperty(ref isEditingInputRanges, value))
            {
                RaiseInputRangePropertiesChanged();
            }
        }
    }

    public bool CanEditInputRanges =>
        deviceSession.IsReadyForSetup &&
        runtime.State.State is not AcquisitionState.Starting and not AcquisitionState.Previewing and
        not AcquisitionState.Recording and not AcquisitionState.Paused and not AcquisitionState.Stopping &&
        ReferenceRangesVolts.Count > 0 && BipolarRangesVolts.Count > 0;

    public bool CanConfirmInputRangeEdit =>
        IsEditingInputRanges &&
        SelectedReferenceRangeVolts is { } referenceRange &&
        SelectedBipolarRangeVolts is { } bipolarRange &&
        AntEegoRangePair.IsCompatible(referenceRange, bipolarRange);

    public bool HasValidStartSetup =>
        deviceSession.IsReadyForSetup &&
        SelectedSamplingRateHz is > 0 &&
        SamplingRatesHz.Contains(SelectedSamplingRateHz.Value) &&
        SelectedReferenceRangeVolts is { } referenceRange &&
        SelectedBipolarRangeVolts is { } bipolarRange &&
        AntEegoRangePair.IsCompatible(referenceRange, bipolarRange);

    public string InputRangeSelectionModeText => isManualInputRangeSelection ? "手动设置" : "自动选择";

    public string ReferenceInputRangeText => FormatInputRange(SelectedReferenceRangeVolts);

    public string BipolarInputRangeText => FormatInputRange(SelectedBipolarRangeVolts);

    public string SupportedSamplingRatesText => SamplingRatesHz.Count == 0
        ? "设备连接后读取"
        : string.Join("、", SamplingRatesHz) + " Hz";

    public int ReferenceInputCount => CountInputChannels(AcquisitionChannelKind.Reference);

    public int BipolarInputCount => CountInputChannels(AcquisitionChannelKind.Bipolar);

    public string DeviceConnectionStatusText => deviceSession.Snapshot.State switch
    {
        DeviceSessionState.Connected => "已连接",
        DeviceSessionState.Streaming => "数据流已打开",
        DeviceSessionState.Impedance => "阻抗检测",
        DeviceSessionState.Discovering => "检测中",
        DeviceSessionState.Faulted => "连接异常",
        _ => "未连接",
    };

    public bool IsDeviceConnected => deviceSession.Snapshot.State is
        DeviceSessionState.Connected or DeviceSessionState.Streaming or DeviceSessionState.Impedance;

    public string DeviceVendorText => DescribeDeviceVendor(
        deviceSession.SelectedDevice?.DriverId ?? deviceSession.Snapshot.DriverId);

    public string DeviceModelText => deviceSession.SelectedDevice?.Model ?? "SDK 未报告型号";

    public string DeviceSerialNumberText => deviceSession.SelectedDevice?.SerialNumber
        ?? deviceSession.SelectedDevice?.DeviceInstanceId
        ?? "SDK 未报告序列号";

    public string DeviceInputCapabilitySummary => deviceSession.SelectedDevice is null
        ? "设备连接后读取"
        : $"参考输入 {ReferenceInputCount} 路，双极输入 {BipolarInputCount} 路；{OtherInputCapabilitiesText}";

    public string OtherInputCapabilitiesText
    {
        get
        {
            var capabilities = deviceSession.SelectedDevice?.ChannelCapabilities;
            if (capabilities is not { Count: > 0 }) return "设备连接后读取";

            var groups = capabilities
                .Where(capability => capability.Kind is not AcquisitionChannelKind.Reference and not AcquisitionChannelKind.Bipolar and not AcquisitionChannelKind.Unknown)
                .GroupBy(capability => capability.Kind)
                .OrderBy(group => group.Key)
                .Select(group => $"{DescribeInputKind(group.Key)} {group.Count()} 路")
                .ToArray();
            return groups.Length == 0 ? "无其他输入" : string.Join("、", groups);
        }
    }

    public void BeginInputRangeEdit()
    {
        if (CanEditInputRanges)
        {
            IsEditingInputRanges = true;
        }
    }

    public void CancelInputRangeEdit()
    {
        SelectedReferenceRangeVolts = automaticReferenceRangeVolts;
        SelectedBipolarRangeVolts = automaticBipolarRangeVolts;
        IsEditingInputRanges = false;
        isManualInputRangeSelection = false;
        RaiseInputRangePropertiesChanged();
    }

    public void ConfirmInputRangeEdit()
    {
        if (SelectedReferenceRangeVolts is not { } referenceRange ||
            SelectedBipolarRangeVolts is not { } bipolarRange ||
            !AntEegoRangePair.IsCompatible(referenceRange, bipolarRange))
        {
            throw new InvalidOperationException("请选择一组设备支持且彼此兼容的输入量程。");
        }

        isManualInputRangeSelection = true;
        IsEditingInputRanges = false;
        RaiseInputRangePropertiesChanged();
    }

    public void RecordPersistedSelection(int samplingRateHz, double referenceRangeVolts, double bipolarRangeVolts)
    {
        savedSamplingRateHz = samplingRateHz;
        savedReferenceRangeText = referenceRangeVolts.ToString("R", CultureInfo.InvariantCulture);
        savedBipolarRangeText = bipolarRangeVolts.ToString("R", CultureInfo.InvariantCulture);
    }

    public bool PrepareDeviceSetup(AcquisitionDeviceDescriptor? device)
    {
        if (DeviceSessionDiscoveryComparer.AreEquivalent(
                appliedSetupDevice is null ? [] : [appliedSetupDevice],
                device is null ? [] : [device]))
        {
            return false;
        }

        Replace(SamplingRatesHz, device?.SupportedSamplingRatesHz.Order() ?? Enumerable.Empty<int>());
        Replace(ReferenceRangesVolts, device?.ReferenceRangesVolts?.Order() ?? Enumerable.Empty<double>());
        availableBipolarRanges = device?.BipolarRangesVolts?.Order().ToArray() ?? [];
        selectedSamplingRateHz = savedSamplingRateHz is { } savedSamplingRate &&
            SamplingRatesHz.Contains(savedSamplingRate)
            ? savedSamplingRate
            : SamplingRatesHz.Count == 1 ? SamplingRatesHz[0] : null;
        RestoreOrChooseRangePair();
        automaticReferenceRangeVolts = selectedReferenceRangeVolts;
        automaticBipolarRangeVolts = selectedBipolarRangeVolts;
        isManualInputRangeSelection = false;
        isEditingInputRanges = false;
        RaisePropertyChanged(nameof(SelectedSamplingRateHz));
        RaisePropertyChanged(nameof(SelectedReferenceRangeVolts));
        RaisePropertyChanged(nameof(SelectedBipolarRangeVolts));
        RaiseCapabilityPropertiesChanged();
        RaiseInputRangePropertiesChanged();
        return true;
    }

    public void MarkDeviceApplied(AcquisitionDeviceDescriptor? device) => appliedSetupDevice = device;

    public void InvalidateSelection()
    {
        SamplingRatesHz.Clear();
        ReferenceRangesVolts.Clear();
        BipolarRangesVolts.Clear();
        availableBipolarRanges = [];
        automaticReferenceRangeVolts = null;
        automaticBipolarRangeVolts = null;
        isManualInputRangeSelection = false;
        isEditingInputRanges = false;
        RaiseCapabilityPropertiesChanged();
        RaiseInputRangePropertiesChanged();
    }

    public void RaiseCapabilityPropertiesChanged()
    {
        RaisePropertyChanged(nameof(DeviceConnectionStatusText));
        RaisePropertyChanged(nameof(IsDeviceConnected));
        RaisePropertyChanged(nameof(DeviceVendorText));
        RaisePropertyChanged(nameof(DeviceModelText));
        RaisePropertyChanged(nameof(DeviceSerialNumberText));
        RaisePropertyChanged(nameof(DeviceInputCapabilitySummary));
        RaisePropertyChanged(nameof(SupportedSamplingRatesText));
        RaisePropertyChanged(nameof(ReferenceInputCount));
        RaisePropertyChanged(nameof(BipolarInputCount));
        RaisePropertyChanged(nameof(OtherInputCapabilitiesText));
        RaisePropertyChanged(nameof(CanEditInputRanges));
    }

    public void RaiseInputRangePropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsEditingInputRanges));
        RaisePropertyChanged(nameof(InputRangeSelectionModeText));
        RaisePropertyChanged(nameof(ReferenceInputRangeText));
        RaisePropertyChanged(nameof(BipolarInputRangeText));
        RaisePropertyChanged(nameof(CanEditInputRanges));
        RaisePropertyChanged(nameof(CanConfirmInputRangeEdit));
    }

    private void RefreshBipolarRangeChoices()
    {
        var compatibleRanges = selectedReferenceRangeVolts is { } referenceRange
            ? AntEegoRangePair.GetCompatibleBipolarRanges(referenceRange, availableBipolarRanges)
            : [];
        Replace(BipolarRangesVolts, compatibleRanges);

        var restored = RestoreRange(savedBipolarRangeText, BipolarRangesVolts);
        selectedBipolarRangeVolts = BipolarRangesVolts.Contains(selectedBipolarRangeVolts ?? double.NaN)
            ? selectedBipolarRangeVolts
            : restored ?? (BipolarRangesVolts.Count == 1 ? BipolarRangesVolts[0] : null);
        RaisePropertyChanged(nameof(SelectedBipolarRangeVolts));
    }

    private void RestoreOrChooseRangePair()
    {
        var savedReference = RestoreRange(savedReferenceRangeText, ReferenceRangesVolts);
        var savedBipolar = RestoreRange(savedBipolarRangeText, availableBipolarRanges);
        var pair = savedReference is { } referenceRange && savedBipolar is { } bipolarRange &&
            AntEegoRangePair.IsCompatible(referenceRange, bipolarRange)
            ? (ReferenceRangeVolts: referenceRange, BipolarRangeVolts: bipolarRange)
            : AntEegoRangePair.FindFirstCompatiblePair(ReferenceRangesVolts, availableBipolarRanges);

        selectedReferenceRangeVolts = pair?.ReferenceRangeVolts;
        Replace(
            BipolarRangesVolts,
            pair is null
                ? []
                : AntEegoRangePair.GetCompatibleBipolarRanges(pair.Value.ReferenceRangeVolts, availableBipolarRanges));
        selectedBipolarRangeVolts = pair?.BipolarRangeVolts;
    }

    private int CountInputChannels(AcquisitionChannelKind kind) =>
        deviceSession.SelectedDevice?.ChannelCapabilities?.Count(capability => capability.Kind == kind) ?? 0;

    private static double? RestoreRange(string savedValue, IReadOnlyCollection<double> supportedValues) =>
        double.TryParse(savedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
        supportedValues.Contains(parsed)
            ? parsed
            : null;

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static string DescribeInputKind(AcquisitionChannelKind kind) => kind switch
    {
        AcquisitionChannelKind.Trigger => "Trigger",
        AcquisitionChannelKind.ImpedanceReference => "阻抗参考",
        AcquisitionChannelKind.ImpedanceGround => "阻抗接地",
        AcquisitionChannelKind.SampleCounter => "采样计数",
        AcquisitionChannelKind.Accelerometer => "加速度",
        AcquisitionChannelKind.Gyroscope => "陀螺仪",
        AcquisitionChannelKind.Magnetometer => "磁力计",
        _ => kind.ToString(),
    };

    private static string FormatInputRange(double? volts)
    {
        if (volts is not { } value || value <= 0) return "未读取";
        var microvolts = value * 1_000_000d;
        return microvolts < 1_000_000d
            ? $"±{microvolts:g} µV"
            : $"±{value:g} V";
    }

    private static string DescribeDeviceVendor(string? driverId) =>
        DeviceProductPresentation.ForDriver(driverId, "SDK 未报告厂商");
}
