using System.Text.Json;
using System.Windows;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public sealed partial class AcquisitionWorkspaceViewModel
{
    private AcquisitionImpedanceWindow? impedanceWindow;
    private bool openingImpedance;
    private CancellationTokenSource? impedanceOpeningCancellation;
    private TaskCompletionSource? impedanceOpeningCompletion;

    public async Task ShowImpedanceAsync(Window owner)
    {
        if (impedanceWindow is { } existing) { existing.Activate(); return; }
        if (openingImpedance) return;
        openingImpedance = true;
        RaiseCommandAvailabilityChanged();
        using var cancellation = new CancellationTokenSource();
        impedanceOpeningCancellation = cancellation;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        impedanceOpeningCompletion = completed;
        AcquisitionImpedanceViewModel? model = null;
        try
        {
            var expectedSession = runtime.State.SessionId;
            var expectedRecording = runtime.RecordingSessionId;
            var completedDirectory = runtime.RecordingDirectory;
            var previousState = runtime.State.State;
            var physical = GetImpedancePhysicalSnapshot();
            var device = SelectedDevice ?? throw new InvalidOperationException("请先检测并选择放大器。");
            var identities = physical.Channels.Where(channel => channel.ExpectedKind == AcquisitionChannelKind.Reference)
                .Select(channel => new ImpedanceChannelIdentity(channel.NativeChannelIndex, channel.ExpectedKind)).ToArray();
            var capabilities = device.ChannelCapabilities ?? [];
            if (identities.Length == 0 || identities.Any(identity => !capabilities.Any(channel =>
                channel.NativeChannelIndex == identity.NativeChannelIndex && channel.Kind == identity.Kind)))
                throw new InvalidOperationException("物理通道配置与当前放大器不匹配，请重新选择采集配置。");
            var request = new ImpedanceStreamRequest(Guid.NewGuid(), device.DeviceId, identities);
            request.Validate();
            if (previousState is AcquisitionState.Previewing or AcquisitionState.Recording or AcquisitionState.Paused)
            {
                var recording = previousState is AcquisitionState.Recording or AcquisitionState.Paused;
                if (!OperationConfirmationDialog.Confirm(owner, new("切换到阻抗检测",
                    recording
                        ? $"当前记录 {expectedRecording:N} 将结束并保存。检测期间不采集 EEG，完成后需要重新预览和开始新记录。"
                        : "当前 EEG 预览和实时算法将结束。检测期间不采集 EEG，结束后可重新预览。",
                    recording ? "保存记录并检测" : "结束预览并检测"))) return;
            }
            else
            {
                await deviceSession.ConfigureForStreamAsync(AntEegoAcquisitionDriver.CreateConfiguration(
                    new AntEegoAdapterOptions(SdkLibraryPath.Trim(), null, null,
                        ChannelLabelsByNativeIndex: physical.Channels.ToDictionary(channel => channel.NativeChannelIndex, channel => channel.ElectrodeLabel))),
                    cancellation.Token);
                expectedSession = runtime.State.SessionId;
                expectedRecording = runtime.RecordingSessionId;
            }
            model = new AcquisitionImpedanceViewModel(runtime, physical, request.DetectionSessionId, dispatcher);
            try
            {
                await runtime.StartImpedanceAsync(request, expectedSession, expectedRecording, cancellation.Token);
            }
            finally
            {
                // A successfully finalized recording stays registered even when the subsequent impedance open fails.
                if (previousState is AcquisitionState.Recording or AcquisitionState.Paused && runtime.State.State == AcquisitionState.Stopped)
                    await CompleteStoppedRecordingPresentationAsync(previousState, completedDirectory);
                LiveMonitor.Refresh();
                RaiseCommandAvailabilityChanged();
            }
            cancellation.Token.ThrowIfCancellationRequested();
            var window = new AcquisitionImpedanceWindow(model, runtime, async () =>
            {
                await StartPreparedPreviewAsync();
                (owner as IDesktopNavigation)?.ShowAcquisitionView();
            }, notifications) { Owner = owner };
            impedanceWindow = window;
            window.Closed += (_, _) => { impedanceWindow = null; RaiseCommandAvailabilityChanged(); };
            window.Show();
            model = null;
        }
        catch
        {
            if (runtime.DeviceMode.Mode != AcquisitionDeviceMode.Idle) await runtime.StopImpedanceAsync();
            throw;
        }
        finally
        {
            model?.Dispose();
            openingImpedance = false;
            impedanceOpeningCancellation = null;
            impedanceOpeningCompletion = null;
            completed.TrySetResult();
            RaiseCommandAvailabilityChanged();
        }
    }

    private ChannelConfigurationProfile GetImpedancePhysicalSnapshot()
    {
        if (runtime.StreamMetadata?.HardwareConfiguration is { } hardware &&
            hardware.TryGetValue("channel_configuration_snapshot_json", out var json))
            return JsonSerializer.Deserialize<ChannelConfigurationProfile>(json)
                ?? throw new InvalidOperationException("当前采集会话的物理通道快照无效。");
        var montage = SelectedMontageProfile ?? throw new InvalidOperationException("请先选择包含物理通道配置的采集导联。");
        var physical = montage.ChannelConfigurationSnapshot;
        if (ChannelConfigurationFingerprint.Create(physical) != montage.ChannelConfigurationFingerprint)
            throw new InvalidOperationException("物理通道配置指纹无效，请重新保存采集配置。");
        return physical;
    }

    public async Task CloseImpedanceWindowAsync()
    {
        impedanceOpeningCancellation?.Cancel();
        if (impedanceOpeningCompletion is { } pending) await pending.Task;
        if (impedanceWindow is { } window) await window.StopAndCloseAsync();
        else if (runtime.DeviceMode.Mode is not (AcquisitionDeviceMode.Idle or AcquisitionDeviceMode.Eeg))
            await runtime.StopImpedanceAsync();
    }
}
