namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

using BrainPlatform.Desktop.Shared.Waveform;

public sealed partial class AlgorithmListViewModel
{
    internal IAlgorithmClient WorkbenchClient => client;

    internal WaveformDisplayRequestContext CreateSignalPreviewRequestContext(long viewGeneration, Guid requestId)
    {
        var source = algorithmRecording ?? throw new InvalidOperationException("当前算法工作区没有活动的录音访问租约。");
        var selection = CaptureMontageSelection();
        if (selection.SelectedMontage is null)
            throw new InvalidOperationException("请选择一个可用的分析导联配置。");
        return new(source.Metadata.RecordingId, source.Metadata.Revision, viewGeneration, requestId, selection.Revision);
    }

    internal async Task<WaveformDisplayFrame<SignalPreviewResponse>> LoadSignalPreviewAsync(
        double startSeconds, long viewGeneration, Guid requestId, CancellationToken token)
    {
        var recording = RegisteredRecording ?? throw new InvalidOperationException("请先选择记录。");
        var request = CreateSignalPreviewRequestContext(viewGeneration, requestId);
        var selection = CaptureMontageSelection();
        var montage = selection.SelectedMontage
            ?? throw new InvalidOperationException("请选择一个可用的分析导联配置。");
        var channels = TnkRoleChannels.Select(role => role.SelectedChannel).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray();
        var band = AlgorithmRunConfiguration.ParseFrequencyBandOrThrow(LowFrequencyText, HighFrequencyText, recording.SamplingRateHz);
        var notch = AlgorithmRunConfiguration.ParseNotchFrequency(SelectedNotchFrequency);
        var payload = await client.GetSignalPreviewAsync(recording.Id, new(startSeconds, 30, montage.Id, channels,
            LowHz: band.LowHz, HighHz: band.HighHz, NotchHz: notch > 0 ? notch : null), token);
        return new(request, payload);
    }
}
