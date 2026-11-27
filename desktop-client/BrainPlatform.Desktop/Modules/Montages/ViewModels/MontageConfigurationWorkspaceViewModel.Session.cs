namespace BrainPlatform.Desktop.Modules.Montages.ViewModels;

public sealed partial class MontageConfigurationWorkspaceViewModel
{
    public bool IsSessionDraft => draft.IsSessionDraft;

    public event EventHandler<MontageProfile>? SessionProfileSaved;

    /// <summary>Edits a whole profile without library writes or device refresh.</summary>
    public void BeginSessionProfile(ChannelConfigurationProfile source, MontageProfile? existing = null)
    {
        SelectedProfile = null;
        StatusText = draft.BeginSession(source, existing);
    }
}
