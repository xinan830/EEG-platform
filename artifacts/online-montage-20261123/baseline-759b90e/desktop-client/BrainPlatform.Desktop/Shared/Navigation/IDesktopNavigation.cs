namespace BrainPlatform.Desktop.Shared.Navigation;

/// <summary>Feature navigation boundary exposed by the desktop shell.</summary>
public interface IDesktopNavigation
{
    void ShowSettingsView();
    void ShowChannelListView();
    void ShowChannelDetailView(bool newProfile = false, bool preserveDraft = false);
    void ShowMontageListView();
    void ShowMontageDetailView(bool newProfile = false, bool preserveDraft = false);
    void ShowScreenCalibrationView();
    void ShowEventListView(bool refreshDefinitions = true);
    void ShowEventDefinitionEditorView(
        BrainPlatform.Desktop.Modules.Events.Domain.EventDefinition? definition = null);
    void ShowProjectListView();
    void ShowProjectDetailView();
    void ShowAcquisitionPreparationView();
    void ShowAcquisitionView();
    Task<bool> ConfirmChannelDraftLeaveAsync();
    Task ShowRecordingReviewViewAsync(
        BrainPlatform.Desktop.Modules.Projects.ViewModels.ProjectRecordingRow recording,
        IEnumerable<BrainPlatform.Desktop.Modules.Montages.Domain.MontageProfile> profiles);
}
