namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel
{
    internal IAlgorithmClient WorkbenchClient => client;
    internal Task<SignalPreviewResponse> LoadSignalPreviewAsync(double startSeconds, CancellationToken token)
    {
        var recording = RegisteredRecording ?? throw new InvalidOperationException("请先选择记录。");
        var channels = TnkRoleChannels.Select(role => role.SelectedChannel).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray();
        var band = AlgorithmRunConfiguration.ParseFrequencyBandOrThrow(LowFrequencyText, HighFrequencyText, recording.SamplingRateHz);
        var notch = AlgorithmRunConfiguration.ParseNotchFrequency(SelectedNotchFrequency);
        return client.GetSignalPreviewAsync(recording.Id, new(startSeconds, 30, SelectedMontageProfile?.Id, channels,
            LowHz: band.LowHz, HighHz: band.HighHz, NotchHz: notch > 0 ? notch : null), token);
    }
}
