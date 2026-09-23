using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;

namespace BrainPlatform.Desktop.ViewModels;

/// <summary>
/// Presents only physical EEG-capable channels returned by the device. Labels
/// are user-confirmed metadata; this class never infers a cap layout.
/// </summary>
public sealed class ChannelMappingViewModel : ObservableObject
{
    private readonly ChannelLabelMappingStore store;
    private readonly BdfChannelTemplateReader bdfTemplateReader;
    private readonly ChannelDisplaySelectionStore displaySelectionStore;
    private readonly ActiveChannelConfigurationStore activeConfigurationStore;
    private readonly OperationNotificationCenter? notifications;
    private IReadOnlyList<AcquisitionChannelCapability> deviceCapabilities = [];
    private string? deviceKey;
    private string? deviceDisplayName;
    private string? deviceDriverId;
    private string? deviceModel;
    private string hardwareReferenceElectrodeLocation = "REF";
    private string hardwareGroundElectrodeLocation = "GND";
    private ChannelConfigurationProfile? activeConfiguration;
    private bool applyingConfiguration;
    private string statusText = "请先测试连接并选择放大器，再设置电极标签。";

    public ChannelMappingViewModel(
        ChannelLabelMappingStore? store = null,
        BdfChannelTemplateReader? bdfTemplateReader = null,
        ChannelDisplaySelectionStore? displaySelectionStore = null,
        ActiveChannelConfigurationStore? activeConfigurationStore = null,
        OperationNotificationCenter? notifications = null)
    {
        this.store = store ?? new ChannelLabelMappingStore();
        this.bdfTemplateReader = bdfTemplateReader ?? new BdfChannelTemplateReader();
        this.displaySelectionStore = displaySelectionStore ?? new ChannelDisplaySelectionStore();
        this.activeConfigurationStore = activeConfigurationStore ?? new ActiveChannelConfigurationStore();
        this.notifications = notifications;
        ImportBdfTemplateCommand = new AsyncRelayCommand(ImportBdfTemplateAsync, ReportCommandError);
    }

    public ObservableCollection<ChannelLabelMappingRow> Rows { get; } = [];

    /// <summary>All channels reported by the opened device, including non-EEG inputs.</summary>
    public ObservableCollection<ChannelLabelMappingRow> AllRows { get; } = [];

    public ObservableCollection<ChannelLabelMappingRow> DisplayRows { get; } = [];

    public event EventHandler? DisplayChannelsChanged;

    public ICommand ImportBdfTemplateCommand { get; }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public bool CanConfigure => deviceKey is not null && Rows.Count > 0;

    public int ActualEegInputCount => Rows.Count;

    public int NamedElectrodeCount => DisplayRows.Count;

    public int SelectedDisplayChannelCount => DisplayRows.Count(row => row.IsSelectedForDisplay);

    public string? CurrentDeviceKey => deviceKey;

    public string? CurrentDeviceDisplayName => deviceDisplayName;

    public string? CurrentDeviceDriverId => deviceDriverId;

    public string? CurrentDeviceModel => deviceModel;

    public string CurrentHardwareReferenceElectrodeLocation => hardwareReferenceElectrodeLocation;

    public string CurrentHardwareGroundElectrodeLocation => hardwareGroundElectrodeLocation;

    public ChannelConfigurationProfile? ActiveConfiguration => activeConfiguration;

    public string? ActiveConfigurationId => activeConfiguration?.Id;

    public bool HasLoadedDevice => deviceKey is not null && Rows.Count > 0;

    public void LoadDevice(AcquisitionDeviceDescriptor? device)
    {
        Rows.Clear();
        AllRows.Clear();
        DisplayRows.Clear();
        deviceKey = null;
        deviceDisplayName = null;
        deviceDriverId = null;
        deviceModel = null;
        hardwareReferenceElectrodeLocation = "REF";
        hardwareGroundElectrodeLocation = "GND";
        activeConfiguration = null;
        deviceCapabilities = [];
        if (device is null)
        {
            StatusText = "请先选择已检测到的放大器。";
            RaisePropertyChanged(nameof(CanConfigure));
            RaisePropertyChanged(nameof(CurrentDeviceDisplayName));
            RaisePropertyChanged(nameof(CurrentDeviceDriverId));
            RaisePropertyChanged(nameof(CurrentDeviceModel));
            RaiseChannelCountsChanged();
            DisplayChannelsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var capabilities = device.ChannelCapabilities ?? [];
        deviceCapabilities = capabilities;
        var eegChannels = capabilities
            .Where(capability => capability.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
            .OrderBy(capability => capability.NativeChannelIndex)
            .ToArray();
        if (eegChannels.Length == 0)
        {
            StatusText = "该设备没有返回可标注的参考或双极 EEG 通道。";
            RaisePropertyChanged(nameof(CanConfigure));
            return;
        }

        deviceDriverId = AcquisitionDeviceCompatibility.NormalizeDriverId(device.DriverId);
        deviceKey = AcquisitionDeviceCompatibility.CreateSignature(deviceDriverId, capabilities);
        deviceDisplayName = device.DisplayName;
        deviceModel = device.Model;
        var hasSavedMapping = store.TryLoad(deviceKey, out var saved);
        var hasSavedDisplaySelection = displaySelectionStore.TryLoad(deviceKey, out var savedDisplaySelection) &&
            savedDisplaySelection.Count > 0;
        IReadOnlyDictionary<int, string> defaultMapping = new Dictionary<int, string>();
        var hasDefaultMapping = !hasSavedMapping && DefaultChannelLabelTemplate.TryGetFor(eegChannels, out defaultMapping);
        var labels = hasSavedMapping ? saved : hasDefaultMapping ? defaultMapping : new Dictionary<int, string>();
        var eegIndexes = eegChannels.Select(channel => channel.NativeChannelIndex).ToHashSet();
        foreach (var capability in capabilities.OrderBy(capability => capability.NativeChannelIndex))
        {
            var isEeg = eegIndexes.Contains(capability.NativeChannelIndex);
            var label = isEeg ? labels.GetValueOrDefault(capability.NativeChannelIndex) ?? string.Empty : string.Empty;
            var row = new ChannelLabelMappingRow(
                capability.NativeChannelIndex,
                capability.Kind.ToString(),
                capability.Unit,
                label,
                isEeg && hasSavedDisplaySelection
                    ? savedDisplaySelection.Contains(capability.NativeChannelIndex)
                    : isEeg && !string.IsNullOrWhiteSpace(label),
                isEeg,
                capability.NativeChannelIndex);
            row.PropertyChanged += OnRowPropertyChanged;
            AllRows.Add(row);
            if (isEeg)
            {
                Rows.Add(row);
            }
        }
        RefreshDisplayRows();

        StatusText = hasSavedMapping
            ? "已加载此设备保存的通道标签；采集前请在准备页选择导联配置。"
            : hasDefaultMapping
                ? "已读取系统默认通道模板；采集前请在准备页选择导联配置。"
                : $"{Rows.Count} 个实际 EEG 通道可标注；采集前请在准备页选择导联配置。";
        RaisePropertyChanged(nameof(CanConfigure));
        RaisePropertyChanged(nameof(AllRows));
        RaisePropertyChanged(nameof(CurrentDeviceDisplayName));
        RaisePropertyChanged(nameof(CurrentDeviceDriverId));
        RaisePropertyChanged(nameof(CurrentDeviceModel));
        RaisePropertyChanged(nameof(ActiveConfiguration));
        RaisePropertyChanged(nameof(ActiveConfigurationId));
        RaiseChannelCountsChanged();
        DisplayChannelsChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyDictionary<int, string> GetCurrentMapping()
    {
        var mapping = new Dictionary<int, string>();
        foreach (var row in Rows)
        {
            var label = row.ElectrodeLabel.Trim();
            if (!string.IsNullOrEmpty(label))
            {
                mapping.Add(row.NativeChannelIndex, label);
            }
        }

        return mapping;
    }

    public string GetDeviceSignature()
    {
        if (!HasLoadedDevice)
        {
            throw new InvalidOperationException("请先连接并选择放大器。");
        }

        return deviceKey ?? throw new InvalidOperationException("当前设备缺少兼容性签名。");
    }

    public IReadOnlyList<ChannelConfigurationEntry> CaptureConfigurationEntries()
    {
        if (!HasLoadedDevice)
        {
            throw new InvalidOperationException("请先连接并选择放大器。");
        }

        return Rows.Select(row => new ChannelConfigurationEntry(
                row.NativeChannelIndex,
                Enum.Parse<AcquisitionChannelKind>(row.Role),
                row.ElectrodeLabel.Trim(),
                row.IsSelectedForDisplay,
                row.DisplayOrder))
            .ToArray();
    }

    public bool TryCreateDefaultSystemProfile(out ChannelConfigurationProfile profile)
    {
        profile = default!;
        if (!HasLoadedDevice || !DefaultChannelLabelTemplate.TryGetFor(deviceCapabilities, out var labels))
        {
            return false;
        }

        var now = DateTimeOffset.UnixEpoch;
        profile = new ChannelConfigurationProfile(
            "system-ant-standard-28-input",
            "ANT默认通道配置",
            "系统默认；ANT/eego 的 24 Reference + 4 Bipolar 物理输入布局。",
            ChannelConfigurationSource.System,
            GetDeviceSignature(),
            Rows.Select((row, index) => new ChannelConfigurationEntry(
                    row.NativeChannelIndex,
                    Enum.Parse<AcquisitionChannelKind>(row.Role),
                    labels.GetValueOrDefault(row.NativeChannelIndex) ?? string.Empty,
                    labels.ContainsKey(row.NativeChannelIndex),
                    row.DisplayOrder))
                .ToArray(),
            now,
            now,
            deviceDisplayName ?? string.Empty,
            deviceDriverId ?? string.Empty,
            deviceModel ?? string.Empty);
        return true;
    }

    public void ApplyConfiguration(ChannelConfigurationProfile profile)
    {
        ValidateConfigurationForCurrentDevice(profile);
        ApplyConfigurationCore(profile, announce: false);
    }

    public async Task ApplyAndSaveConfigurationAsync(
        ChannelConfigurationProfile profile,
        CancellationToken cancellationToken)
    {
        ValidateConfigurationForCurrentDevice(profile);
        var labels = profile.Channels
            .Where(channel => !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .ToDictionary(channel => channel.NativeChannelIndex, channel => channel.ElectrodeLabel.Trim());
        var displayed = profile.Channels
            .Where(channel => channel.IsSelectedForDisplay && !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .Select(channel => channel.NativeChannelIndex)
            .ToArray();

        // The applied snapshot is authoritative and is written last. Until all
        // persistence succeeds, the published in-memory configuration is unchanged.
        await store.SaveAsync(GetDeviceSignature(), labels, cancellationToken);
        await displaySelectionStore.SaveAsync(GetDeviceSignature(), displayed, cancellationToken);
        await activeConfigurationStore.SaveAsync(profile, cancellationToken);
        ApplyConfigurationCore(profile, announce: false);
    }

    private void ValidateConfigurationForCurrentDevice(ChannelConfigurationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!HasLoadedDevice)
        {
            throw new InvalidOperationException("请先连接并选择放大器。");
        }
        if (!string.Equals(profile.DeviceSignature, GetDeviceSignature(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("该通道配置与当前放大器的物理输入和通道类型不匹配。");
        }
        ChannelConfigurationValidation.Validate(profile);

        var entries = profile.Channels.ToDictionary(entry => entry.NativeChannelIndex);
        if (entries.Count != Rows.Count)
        {
            throw new InvalidOperationException("该通道配置不包含当前设备的完整 EEG 输入集。");
        }
        foreach (var row in Rows)
        {
            if (!entries.TryGetValue(row.NativeChannelIndex, out var entry) ||
                !string.Equals(entry.ExpectedKind.ToString(), row.Role, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("该通道配置包含与当前设备不一致的物理通道。");
            }
        }
    }

    private void ApplyConfigurationCore(ChannelConfigurationProfile profile, bool announce)
    {
        var entries = profile.Channels.ToDictionary(entry => entry.NativeChannelIndex);

        applyingConfiguration = true;
        try
        {
            foreach (var row in Rows)
            {
                var entry = entries[row.NativeChannelIndex];
                row.ElectrodeLabel = entry.ElectrodeLabel;
                row.IsSelectedForDisplay = entry.IsSelectedForDisplay;
                row.DisplayOrder = entry.DisplayOrder;
            }
        }
        finally
        {
            applyingConfiguration = false;
        }

        hardwareReferenceElectrodeLocation = DefaultLocation(profile.HardwareReferenceElectrodeLocation, "REF");
        hardwareGroundElectrodeLocation = DefaultLocation(profile.HardwareGroundElectrodeLocation, "GND");
        activeConfiguration = profile;
        RaisePropertyChanged(nameof(CurrentHardwareReferenceElectrodeLocation));
        RaisePropertyChanged(nameof(CurrentHardwareGroundElectrodeLocation));

        RefreshDisplayRows();
        StatusText = $"已载入通道配置“{profile.Name}”。";
        RaisePropertyChanged(nameof(ActiveConfiguration));
        RaisePropertyChanged(nameof(ActiveConfigurationId));
        if (announce)
        {
            notifications?.PublishSuccess($"已应用通道配置“{profile.Name}”。");
        }
        RaiseChannelCountsChanged();
        DisplayChannelsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync(CancellationToken cancellationToken, bool preserveActiveConfiguration = false)
    {
        if (deviceKey is null)
        {
            throw new InvalidOperationException("请先选择已检测到的放大器。");
        }

        var mapping = GetCurrentMapping();
        var selectedDisplayChannels = DisplayRows
            .Where(row => row.IsSelectedForDisplay)
            .Select(row => row.NativeChannelIndex)
            .ToArray();
        if (selectedDisplayChannels.Length == 0)
        {
            throw new InvalidOperationException("请至少选择一个显示通道。未命名硬件输入不会自动显示。");
        }
        var duplicates = mapping.Values
            .GroupBy(label => label, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException($"电极标签不能重复：{string.Join(", ", duplicates)}。");
        }

        await store.SaveAsync(deviceKey, mapping, cancellationToken);
        await displaySelectionStore.SaveAsync(
            deviceKey,
            selectedDisplayChannels,
            cancellationToken);
        if (!preserveActiveConfiguration)
        {
            await activeConfigurationStore.ClearAsync(deviceKey, cancellationToken);
            activeConfiguration = null;
            RaisePropertyChanged(nameof(ActiveConfiguration));
            RaisePropertyChanged(nameof(ActiveConfigurationId));
        }
        StatusText = mapping.Count == 0
            ? "已保存为空映射；请应用命名配置后再采集。"
            : $"已保存 {mapping.Count} 个用户确认的电极标签；请应用命名配置后再采集。";
    }

    public void ApplyTemplate(BdfChannelTemplate template)
    {
        if (!CanConfigure)
        {
            throw new InvalidOperationException("请先选择已检测到的放大器。");
        }

        var imported = 0;
        foreach (var row in Rows)
        {
            if (template.LabelsByChannelIndex.TryGetValue(row.NativeChannelIndex, out var label))
            {
                row.ElectrodeLabel = label;
                imported++;
            }
            else
            {
                row.ElectrodeLabel = string.Empty;
            }
        }

        StatusText = $"已从 BDF 头导入 {imported} 个通道标签。请确认后点击“保存标注”。";
        RefreshDisplayRows();
        RaiseChannelCountsChanged();
        DisplayChannelsChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task ImportBdfTemplateAsync()
    {
        if (!CanConfigure)
        {
            throw new InvalidOperationException("请先选择已检测到的放大器。");
        }

        var picker = new OpenFileDialog
        {
            Title = "选择已确认通道布局的 BDF 文件",
            Filter = "BDF 文件|*.bdf|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (picker.ShowDialog() == true)
        {
            ApplyTemplate(bdfTemplateReader.Read(picker.FileName));
        }

        return Task.CompletedTask;
    }

    private void ReportCommandError(Exception exception)
    {
        StatusText = exception.Message;
        notifications?.PublishError(exception.Message);
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ChannelLabelMappingRow.ElectrodeLabel))
        {
            RefreshDisplayRows();
        }

        if (eventArgs.PropertyName is nameof(ChannelLabelMappingRow.ElectrodeLabel) or nameof(ChannelLabelMappingRow.IsSelectedForDisplay))
        {
            if (!applyingConfiguration &&
                eventArgs.PropertyName == nameof(ChannelLabelMappingRow.IsSelectedForDisplay) &&
                sender is ChannelLabelMappingRow row)
            {
                var identity = string.IsNullOrWhiteSpace(row.ElectrodeLabel)
                    ? $"设备输入 {row.NativeChannelIndex}"
                    : $"通道“{row.ElectrodeLabel.Trim()}”";
                notifications?.PublishSuccess(row.IsSelectedForDisplay
                    ? $"已显示{identity}。"
                    : $"已隐藏{identity}。");
            }
            RaiseChannelCountsChanged();
            DisplayChannelsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RefreshDisplayRows()
    {
        DisplayRows.Clear();
        foreach (var row in Rows.Where(row => !string.IsNullOrWhiteSpace(row.ElectrodeLabel)))
        {
            DisplayRows.Add(row);
        }
    }

    private void RaiseChannelCountsChanged()
    {
        RaisePropertyChanged(nameof(ActualEegInputCount));
        RaisePropertyChanged(nameof(NamedElectrodeCount));
        RaisePropertyChanged(nameof(SelectedDisplayChannelCount));
    }

    private static string DefaultLocation(string? location, string fallback) =>
        string.IsNullOrWhiteSpace(location) ? fallback : location.Trim();
}

public sealed class ChannelLabelMappingRow : ObservableObject
{
    private string electrodeLabel;
    private bool isSelectedForDisplay;

    public ChannelLabelMappingRow(
        int nativeChannelIndex,
        string role,
        string unit,
        string electrodeLabel,
        bool isSelectedForDisplay = false,
        bool isEegInput = true,
        int displayOrder = 0)
    {
        NativeChannelIndex = nativeChannelIndex;
        Role = role;
        Unit = unit;
        IsEegInput = isEegInput;
        displayOrderValue = displayOrder;
        this.electrodeLabel = electrodeLabel;
        this.isSelectedForDisplay = isSelectedForDisplay;
    }

    public int NativeChannelIndex { get; }

    public string Role { get; }

    public string Unit { get; }

    public bool IsEegInput { get; }

    public bool IsAcquisitionEnabled => true;

    public bool CanEditElectrodeLabel => IsEegInput;

    private int displayOrderValue;

    public int DisplayOrder
    {
        get => IsEegInput ? displayOrderValue : 0;
        set => SetProperty(ref displayOrderValue, IsEegInput ? Math.Max(0, value) : 0);
    }

    public string ElectrodeLabel
    {
        get => electrodeLabel;
        set => SetProperty(ref electrodeLabel, value);
    }

    public bool IsSelectedForDisplay
    {
        get => isSelectedForDisplay;
        set => SetProperty(ref isSelectedForDisplay, value);
    }
}
