using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed partial class TnkDetailViewModel
{
    private WaveformDisplayFrame<SignalPreviewResponse>? waveformFrame;
    private CancellationTokenSource? waveformRequest;
    private long waveformRevision;
    private Guid waveformRequestId;
    private bool waveformLoadInProgress;
    private bool waveformReloadPending;
    private string waveformStatus = "选择记录后显示波形";

    public SignalPreviewResponse? Waveform => waveformFrame?.Payload;
    public string WaveformStatus { get => waveformStatus; private set => SetProperty(ref waveformStatus, value); }

    private void InitializeWaveformCommands()
    {
        LoadWaveformCommand = new AsyncRelayCommand(RequestWaveformLoadAsync, error => WaveformStatus = error.Message);
        PreviousWaveformCommand = new RelayCommand(() => SeekFromChart(WaveformPosition - 10));
        NextWaveformCommand = new RelayCommand(() => SeekFromChart(WaveformPosition + 10));
    }

    private void InvalidateWaveformFrame()
    {
        waveformRevision++;
        waveformRequestId = Guid.Empty;
        waveformFrame = null;
        RaisePropertyChanged(nameof(Waveform));
        ReloadWaveform();
    }

    private void SetWaveformFrame(WaveformDisplayFrame<SignalPreviewResponse>? frame)
    {
        waveformFrame = frame;
        RaisePropertyChanged(nameof(Waveform));
    }

    private void ReloadWaveform()
    {
        if (!workbenchActive) return;
        waveformReloadPending = true;
        if (!waveformLoadInProgress) _ = LoadWaveformSafelyAsync();
    }

    private Task RequestWaveformLoadAsync()
    {
        ReloadWaveform();
        return Task.CompletedTask;
    }

    private async Task LoadWaveformSafelyAsync()
    {
        if (!workbenchActive || waveformLoadInProgress) return;
        waveformLoadInProgress = true;
        try
        {
            do
            {
                waveformReloadPending = false;
                try { await LoadWaveformAsync(); }
                catch (Exception error)
                {
                    if (workbenchActive)
                    {
                        WaveformStatus = error.Message;
                        StopWorkbenchPlayback();
                    }
                }
            }
            while (workbenchActive && waveformReloadPending);
        }
        finally
        {
            waveformLoadInProgress = false;
            if (workbenchActive && waveformReloadPending) _ = LoadWaveformSafelyAsync();
        }
    }

    private async Task LoadWaveformAsync()
    {
        var viewGeneration = ++waveformRevision;
        var requestId = waveformRequestId = Guid.NewGuid();
        waveformRequest?.Cancel();
        waveformRequest?.Dispose();
        using var request = new CancellationTokenSource();
        var token = request.Token;
        waveformRequest = request;
        try
        {
            WaveformStatus = "正在读取当前导联波形…";
            var frame = await Catalog.LoadSignalPreviewAsync(
                Math.Max(0, WaveformPosition - 10), viewGeneration, requestId, token);
            if (viewGeneration != waveformRevision || token.IsCancellationRequested) return;
            var payload = frame.Payload;
            if (payload.RecordingId != Catalog.RegisteredRecording?.Id ||
                payload.MontageId != Catalog.SelectedMontageProfile?.Id)
                throw new InvalidOperationException("波形来源身份已变化，拒绝绑定。");

            var currentRequest = Catalog.CreateSignalPreviewRequestContext(waveformRevision, waveformRequestId);
            if (!WaveformDisplayFramePublisher.TryPublish(
                    frame, currentRequest, token,
                    published => SetWaveformFrame(frame with { Payload = published }))) return;
            WaveformStatus = $"{payload.LowHz:G}–{payload.HighHz:G} Hz · 陷波 {(payload.NotchHz is double notch ? notch + " Hz" : "关闭")} · 显示因果滤波 · 原始记录不变";
        }
        catch (OperationCanceledException) { }
        catch (Exception) when (viewGeneration != waveformRevision || token.IsCancellationRequested) { }
        finally { if (ReferenceEquals(waveformRequest, request)) waveformRequest = null; }
    }
}
