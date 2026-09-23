namespace BrainPlatform.Desktop.Modules.Channels.ViewModels;

public sealed class ChannelConfigurationDraftRow : ObservableObject
{
    private string electrodeLabel;
    private bool isEnabled;
    private int displayOrder;

    public ChannelConfigurationDraftRow(
        int nativeChannelIndex,
        string role,
        string deviceUnit,
        string displayUnit,
        string electrodeLabel,
        bool isEnabled,
        int displayOrder,
        bool isEegInput)
    {
        NativeChannelIndex = nativeChannelIndex;
        Role = role;
        DeviceUnit = deviceUnit;
        DisplayUnit = displayUnit;
        this.electrodeLabel = electrodeLabel;
        this.isEnabled = isEnabled;
        this.displayOrder = displayOrder;
        IsEegInput = isEegInput;
    }

    public int NativeChannelIndex { get; }

    public string Role { get; }

    public string DeviceUnit { get; }

    public string DisplayUnit { get; }

    public bool IsEegInput { get; }

    public bool CanEdit => IsEegInput;

    public string ConfigurationStatus => IsEnabled ? "已显示" : "未显示";

    public string ElectrodeLabel
    {
        get => electrodeLabel;
        set => SetProperty(ref electrodeLabel, value);
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
