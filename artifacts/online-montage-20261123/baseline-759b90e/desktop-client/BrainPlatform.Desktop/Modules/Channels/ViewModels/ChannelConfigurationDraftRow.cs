namespace BrainPlatform.Desktop.Modules.Channels.ViewModels;

using BrainPlatform.Desktop.Modules.Channels.Api;
using BrainPlatform.Desktop.Modules.Channels.Domain;

public sealed class ChannelConfigurationDraftRow : ObservableObject
{
    private string electrodeLabel;
    private bool isEnabled;
    private int displayOrder;
    private ElectrodeLocationSnapshot? electrodeLocation;
    private ElectrodeLocationSnapshot? templateElectrodeLocation;
    private ElectrodeMapPosition? manualMapPosition;
    private long electrodeLabelRevision;

    public ChannelConfigurationDraftRow(
        int nativeChannelIndex,
        string role,
        string deviceUnit,
        string displayUnit,
        string electrodeLabel,
        bool isEnabled,
        int displayOrder,
        bool isEegInput,
        ElectrodeLocationSnapshot? electrodeLocation = null,
        ElectrodeMapPosition? manualMapPosition = null)
    {
        NativeChannelIndex = nativeChannelIndex;
        Role = role;
        DeviceUnit = deviceUnit;
        DisplayUnit = displayUnit;
        this.electrodeLabel = electrodeLabel;
        this.isEnabled = isEnabled;
        this.displayOrder = displayOrder;
        IsEegInput = isEegInput;
        this.electrodeLocation = electrodeLocation;
        templateElectrodeLocation = electrodeLocation;
        this.manualMapPosition = manualMapPosition;
    }

    public int NativeChannelIndex { get; }

    public string Role { get; }

    public string DeviceUnit { get; }

    public string DisplayUnit { get; }

    public bool IsEegInput { get; }

    public bool CanEdit => IsEegInput;

    public long ElectrodeLabelRevision => electrodeLabelRevision;

    public ElectrodeLocationSnapshot? ElectrodeLocation
    {
        get => electrodeLocation;
        private set
        {
            if (SetProperty(ref electrodeLocation, value))
            {
                RaisePropertyChanged(nameof(LocationText));
            }
        }
    }

    public string LocationText => ManualMapPosition is not null
        ? "手动示意位置"
        : ElectrodeLocation is { } location
            ? $"{location.CanonicalSite} · 标准模板"
            : "不可用 / 未解析";

    public ElectrodeMapPosition? ManualMapPosition
    {
        get => manualMapPosition;
        private set
        {
            if (SetProperty(ref manualMapPosition, value))
            {
                RaisePropertyChanged(nameof(LocationText));
            }
        }
    }

    public void SetManualMapPosition(ElectrodeMapPosition? position) => ManualMapPosition = position;

    public void SetElectrodeLocation(ElectrodeLocationSnapshot? location)
    {
        templateElectrodeLocation = location;
        ElectrodeLocation = location;
    }

    /// <summary>Restores the immutable standard-template position and removes the diagram override.</summary>
    public void ResetToTemplatePosition()
    {
        ElectrodeLocation = templateElectrodeLocation;
        ManualMapPosition = null;
    }

    public string ConfigurationStatus => IsEnabled ? "已显示" : "未显示";

    public string ElectrodeLabel
    {
        get => electrodeLabel;
        set
        {
            if (SetProperty(ref electrodeLabel, value))
            {
                electrodeLabelRevision++;
                RaisePropertyChanged(nameof(ElectrodeLabelRevision));
                templateElectrodeLocation = null;
                ElectrodeLocation = null;
                ManualMapPosition = null;
            }
        }
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (SetProperty(ref isEnabled, value))
            {
                RaisePropertyChanged(nameof(ConfigurationStatus));
            }
        }
    }

    public int DisplayOrder
    {
        get => displayOrder;
        set => SetProperty(ref displayOrder, Math.Max(0, value));
    }
}
