namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed partial class RecordingReviewViewModel
{
    private MontageProfile? temporaryWaveformMontage;
    public MontageProfile? TemporaryWaveformMontage => temporaryWaveformMontage;

    public ChannelConfigurationProfile OnlineMontageSource => catalog.AcquisitionMontage?.ChannelConfigurationSnapshot
        ?? new ChannelConfigurationProfile($"review-{recordingId}", "当前记录通道", string.Empty,
            ChannelConfigurationSource.User, reviewReader.Manifest.DeviceId,
            reviewReader.Manifest.Channels
                .Where(channel => channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
                .Where(channel => !string.IsNullOrWhiteSpace(channel.Label))
                .Select((channel, index) => new ChannelConfigurationEntry(channel.NativeChannelIndex,
                    channel.Kind, channel.Label!, true, index)).ToArray(), recordingStartUtc, recordingStartUtc);

    /// <summary>Stores and selects a full waveform-only profile without changing analysis selection.</summary>
    public async Task SetTemporaryWaveformMontageAsync(MontageProfile profile)
    {
        BrainPlatform.Desktop.Modules.Montages.Domain.TemporaryWaveformMontage.ValidateSources(
            profile, catalog.RawSignalView.ChannelLabels);
        temporaryWaveformMontage = profile;
        RaisePropertyChanged(nameof(TemporaryWaveformMontage));
        RaisePropertyChanged(nameof(CompatibleViewingMontages));
        RaisePropertyChanged(nameof(HasCompatibleViewingMontages));
        await session.SetTemporaryMontageProfileAsync(profile);
        RaisePropertyChanged(nameof(CurrentViewingMontage));
    }
}
