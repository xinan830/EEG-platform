using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel
{
    private void InitializeAlgorithmMontageSelection(string recordingId)
    {
        ClearAlgorithmMontageSelection();
        var profiles = MontageProfiles.Where(profile => profile.Available).ToArray();
        var acquisition = profiles.FirstOrDefault(profile => profile.IsAcquisitionDefault);
        montageSelection = new WorkspaceMontageSelection<HistoricalMontageDescriptor>(
            recordingId, acquisition, profiles);
        montageSelection.SelectionChanged += OnAlgorithmMontageSelectionChanged;
        ApplyAlgorithmMontageSelection(null, montageSelection.Capture());
        RaisePropertyChanged(nameof(MontageSelection));
        RaisePropertyChanged(nameof(SelectedMontageProfile));
        RaisePropertyChanged(nameof(AnalysisMontageText));
        RaisePropertyChanged(nameof(AnalysisMontageDetailsText));
    }

    private void ClearAlgorithmMontageSelection()
    {
        if (montageSelection is null)
            return;

        InvalidateAlgorithmConfiguration();
        montageSelection.SelectionChanged -= OnAlgorithmMontageSelectionChanged;
        montageSelection = null;
        RaisePropertyChanged(nameof(MontageSelection));
        RaisePropertyChanged(nameof(SelectedMontageProfile));
        RaisePropertyChanged(nameof(AnalysisMontageText));
        RaisePropertyChanged(nameof(AnalysisMontageDetailsText));
    }

    private void OnAlgorithmMontageSelectionChanged(
        object? sender,
        WorkspaceMontageSelectionChangedEventArgs<HistoricalMontageDescriptor> args)
    {
        if (!ReferenceEquals(sender, montageSelection) || disposed)
            return;

        ApplyAlgorithmMontageSelection(args.Previous, args.Current);
        RaisePropertyChanged(nameof(SelectedMontageProfile));
        RaisePropertyChanged(nameof(AnalysisMontageText));
        RaisePropertyChanged(nameof(AnalysisMontageDetailsText));
        ScheduleAutomaticRun();
    }

    private void ApplyAlgorithmMontageSelection(
        WorkspaceMontageSelectionSnapshot<HistoricalMontageDescriptor>? previous,
        WorkspaceMontageSelectionSnapshot<HistoricalMontageDescriptor> current)
    {
        var previousProfile = previous?.SelectedMontage;
        var currentProfile = current.SelectedMontage;
        var previousRole = previousProfile?.Outputs.FirstOrDefault(output =>
            string.Equals(output.Id, SelectedChannel, StringComparison.OrdinalIgnoreCase))?.Role
            ?? SelectedChannel;
        var previousSecondaryRole = previousProfile?.Outputs.FirstOrDefault(output =>
            string.Equals(output.Id, SelectedF4Channel, StringComparison.OrdinalIgnoreCase))?.Role
            ?? SelectedF4Channel;

        if (currentProfile is null)
        {
            RegisteredChannels.Clear();
            SelectedChannel = string.Empty;
            SelectedF4Channel = string.Empty;
            RefreshTnkRoleChannels();
            RaisePropertyChanged(nameof(RegisteredChannels));
            RaisePropertyChanged(nameof(HasRegisteredChannels));
            return;
        }

        RegisteredChannels.Clear();
        foreach (var output in currentProfile.Outputs
                     .Where(output => output.Available)
                     .Select(output => output.Id)
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            RegisteredChannels.Add(output);

        var mapped = currentProfile.Outputs.FirstOrDefault(output =>
            output.Available && string.Equals(output.Role, previousRole, StringComparison.OrdinalIgnoreCase))?.Id;
        SelectedChannel = previous is null
            ? mapped ?? RegisteredChannels.FirstOrDefault() ?? string.Empty
            : mapped ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(previousSecondaryRole))
        {
            var mappedSecondary = currentProfile.Outputs.FirstOrDefault(output =>
                output.Available && string.Equals(output.Role, previousSecondaryRole, StringComparison.OrdinalIgnoreCase))?.Id;
            SelectedF4Channel = mappedSecondary ?? string.Empty;
        }

        RefreshTnkRoleChannels();
        RaisePropertyChanged(nameof(RegisteredChannels));
        RaisePropertyChanged(nameof(HasRegisteredChannels));
    }

    private void InvalidateAlgorithmConfiguration()
    {
        configurationRevision++;
        runCoordinator.SupersedeForeground(configurationRevision);
        automaticRunPending = false;
        automaticRunDebounceTimer.Stop();
        InvalidateBackgroundFullRunDisplay();
        if (IsOnDemandAnalysis)
            ClearWindowAnalysis();
    }
}
