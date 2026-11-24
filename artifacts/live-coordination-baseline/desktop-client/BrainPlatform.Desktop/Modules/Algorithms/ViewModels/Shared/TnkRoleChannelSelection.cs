namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed class TnkRoleChannelSelection : ObservableObject
{
    private string selectedChannel = string.Empty;

    internal TnkRoleChannelSelection(string key, string label, string preferredChannel)
    {
        Key = key;
        Label = label;
        SelectedChannel = preferredChannel;
    }

    public string Key { get; }
    public string Label { get; }
    public string SelectedChannel
    {
        get => selectedChannel;
        set
        {
            if (!SetProperty(ref selectedChannel, value)) return;
            RaisePropertyChanged(nameof(DisplayText));
        }
    }

    public string DisplayText => $"{Key} → {(string.IsNullOrWhiteSpace(SelectedChannel) ? "未选择" : SelectedChannel)}";
}
