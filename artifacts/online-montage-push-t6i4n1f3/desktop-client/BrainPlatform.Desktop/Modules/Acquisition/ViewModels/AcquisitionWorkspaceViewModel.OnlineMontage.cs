namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public sealed partial class AcquisitionWorkspaceViewModel
{
    private MontageProfile? temporaryWaveformMontage;
    private MontageProfile? regularDisplayMontage;

    public MontageProfile? TemporaryWaveformMontage => temporaryWaveformMontage;
    public ChannelConfigurationProfile? OnlineMontageSource => SelectedMontageProfile?.ChannelConfigurationSnapshot;
    public bool CanSelectWaveformMontage => CanSwitchLiveDisplayMontage ||
        (temporaryWaveformMontage is not null && LiveMonitor.HasOpenStream);

    public IReadOnlyList<MontageProfile> FilterCompatibleLiveDisplayMontages(IEnumerable<MontageProfile> candidates)
    {
        var profiles = candidates.Where(IsCompatibleLiveDisplayMontage)
            .Where(profile => CanSwitchLiveDisplayMontage || temporaryWaveformMontage is null || ReferenceEquals(profile, regularDisplayMontage))
            .ToList();
        if (temporaryWaveformMontage is { } temporary)
        {
            if (regularDisplayMontage is { } regular && !profiles.Contains(regular)) profiles.Add(regular);
            profiles.Add(temporary);
        }
        return profiles;
    }

    /// <summary>Replaces the whole waveform configuration without notifying the algorithm coordinator.</summary>
    public void SetTemporaryWaveformMontage(MontageProfile? profile)
    {
        if (profile is null) { ClearTemporaryWaveformMontage(); return; }
        if (!LiveMonitor.HasOpenStream) throw new InvalidOperationException("请先开始采集预览。");
        BrainPlatform.Desktop.Modules.Montages.Domain.TemporaryWaveformMontage.ValidateSources(
            profile, LiveMonitor.Channels.Select(channel => channel.Label));
        if (temporaryWaveformMontage is null || !ReferenceEquals(liveDisplayMontageProfile, temporaryWaveformMontage))
            regularDisplayMontage = liveDisplayMontageProfile;
        temporaryWaveformMontage = profile;
        RaisePropertyChanged(nameof(TemporaryWaveformMontage));
        SelectWaveformOnly(profile);
        RaisePropertyChanged(nameof(CanSelectWaveformMontage));
    }

    private bool TrySelectTemporaryDisplayMontage(MontageProfile? profile)
    {
        if (profile is null || temporaryWaveformMontage is null) return false;
        if (ReferenceEquals(profile, temporaryWaveformMontage) ||
            (!CanSwitchLiveDisplayMontage && ReferenceEquals(profile, regularDisplayMontage)))
        {
            if (LiveMonitor.HasOpenStream) SelectWaveformOnly(profile);
            return true;
        }
        return false;
    }

    private void SelectWaveformOnly(MontageProfile profile)
    {
        liveDisplayMontageProfile = profile;
        LiveMonitor.ConfigureMontage(profile);
        RaisePropertyChanged(nameof(LiveDisplayMontageProfile));
    }

    private void ClearTemporaryWaveformMontage()
    {
        if (temporaryWaveformMontage is null) return;
        if (ReferenceEquals(liveDisplayMontageProfile, temporaryWaveformMontage) && regularDisplayMontage is { } regular)
            SelectWaveformOnly(regular);
        temporaryWaveformMontage = null;
        regularDisplayMontage = null;
        RaisePropertyChanged(nameof(TemporaryWaveformMontage));
        RaisePropertyChanged(nameof(CanSelectWaveformMontage));
    }
}
