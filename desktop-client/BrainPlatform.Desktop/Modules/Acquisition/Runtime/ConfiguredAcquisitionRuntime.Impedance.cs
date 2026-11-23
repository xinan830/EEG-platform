using BrainPlatform.Desktop.Infrastructure.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Acquisition.Runtime;

public enum AcquisitionDeviceMode { Idle, Eeg, OpeningImpedance, Impedance, ClosingImpedance, ImpedanceFaulted }

public sealed record AcquisitionDeviceModeSnapshot(AcquisitionDeviceMode Mode, Guid? DetectionSessionId, string Detail);

public sealed partial class ConfiguredAcquisitionRuntime
{
    private ImpedanceReadingSession? impedanceSession;
    private volatile AcquisitionDeviceModeSnapshot impedanceMode = new(AcquisitionDeviceMode.Idle, null, "设备空闲");
    public event EventHandler<AcquisitionDeviceModeSnapshot>? DeviceModeChanged;
    public event EventHandler<ImpedanceSnapshot>? ImpedanceUpdated;
    public AcquisitionDeviceModeSnapshot DeviceMode => impedanceMode.Mode != AcquisitionDeviceMode.Idle
        ? impedanceMode
        : new(IsEegActive ? AcquisitionDeviceMode.Eeg : AcquisitionDeviceMode.Idle, null, State.Detail);
    private bool IsEegActive => State.State is AcquisitionState.Starting or AcquisitionState.Previewing or
        AcquisitionState.Recording or AcquisitionState.Paused or AcquisitionState.Stopping;
    public bool IsDeviceOccupied => IsEegActive || impedanceMode.Mode != AcquisitionDeviceMode.Idle;

    /// <summary>Confirmation identities are rechecked under the same lock as stop/finalize/open.</summary>
    public async Task StartImpedanceAsync(ImpedanceStreamRequest request, Guid? expectedSessionId,
        Guid? expectedRecordingId, CancellationToken cancellationToken)
    {
        request.Validate();
        await transition.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            EnsureNoImpedance();
            if (State.SessionId != expectedSessionId || RecordingSessionId != expectedRecordingId)
                throw new AcquisitionUnavailableException("采集会话已变化，请重新确认后再进入阻抗检测。");
            if (State.State is AcquisitionState.Faulted or AcquisitionState.Starting or AcquisitionState.Stopping)
                throw new AcquisitionUnavailableException("请先处理采集错误或等待当前操作完成。");
            var capability = adapter as IAcquisitionImpedanceDeviceAdapter
                ?? throw new AcquisitionUnavailableException("当前设备驱动未提供阻抗检测能力。");
            if (capability.ImpedanceCapability == ImpedanceCapabilityStatus.Unsupported)
                throw new AcquisitionUnavailableException("当前 SDK 不支持阻抗检测。");
            SetImpedanceMode(AcquisitionDeviceMode.OpeningImpedance, request.DetectionSessionId, "正在结束 EEG 并打开阻抗检测…");
            if (IsEegActive)
            {
                await RequireCoordinator().StopAsync(cancellationToken);
                if (State.State != AcquisitionState.Stopped)
                    throw new AcquisitionUnavailableException("EEG 尚未安全结束，不能进入阻抗检测。");
                if (expectedSessionId is { } sessionId && liveFilterBridge is not null)
                    await liveFilterBridge.CloseAsync(sessionId, cancellationToken);
            }
            var stream = await Task.Run(() => capability.OpenImpedanceStreamAsync(request, cancellationToken), cancellationToken);
            impedanceSession = new ImpedanceReadingSession(stream, request.DetectionSessionId);
            impedanceSession.Updated += ForwardImpedance;
            impedanceSession.Failed += OnImpedanceFailed;
            SetImpedanceMode(AcquisitionDeviceMode.Impedance, request.DetectionSessionId, "阻抗检测中 · EEG 已停止");
            impedanceSession.Start();
        }
        catch (Exception exception)
        {
            // Preserve uncertainty after a failed open; only an explicit successful cleanup clears it.
            if (impedanceMode.Mode == AcquisitionDeviceMode.OpeningImpedance)
                SetImpedanceMode(AcquisitionDeviceMode.ImpedanceFaulted, request.DetectionSessionId, exception.Message);
            throw;
        }
        finally { transition.Release(); }
    }

    public async Task StopImpedanceAsync(CancellationToken cancellationToken = default)
    {
        await transition.WaitAsync(cancellationToken);
        try { await StopImpedanceCoreAsync(); }
        finally { transition.Release(); }
    }

    private async Task StopImpedanceCoreAsync()
    {
        if (impedanceMode.Mode == AcquisitionDeviceMode.Idle) return;
        SetImpedanceMode(AcquisitionDeviceMode.ClosingImpedance, impedanceMode.DetectionSessionId, "正在停止阻抗检测…");
        try
        {
            if (impedanceSession is { } session)
            {
                await session.StopAsync();
                session.Updated -= ForwardImpedance;
                session.Failed -= OnImpedanceFailed;
                impedanceSession = null;
            }
            else
            {
                if (IsEegActive) await RequireCoordinator().StopAsync(CancellationToken.None);
                if (State.State is AcquisitionState.Faulted or AcquisitionState.Stopping)
                    throw new AcquisitionUnavailableException("EEG 清理尚未完成，设备保持占用；请先处理采集错误。");
                // Retry retained native ownership after a partial open failure.
                await Task.Run(DisposeConfiguredRuntimeAsync);
                if (configuredDriver is { } configuration) CreateConfiguredRuntime(configuration);
            }
            SetImpedanceMode(AcquisitionDeviceMode.Idle, null, "检测已结束；可以重新开始 EEG 预览。");
        }
        catch (Exception exception)
        {
            SetImpedanceMode(AcquisitionDeviceMode.ImpedanceFaulted, impedanceMode.DetectionSessionId, exception.Message);
            throw;
        }
    }

    private void EnsureNoImpedance()
    {
        ThrowIfDisposed();
        if (impedanceMode.Mode != AcquisitionDeviceMode.Idle)
            throw new AcquisitionDeviceBusyException("请先结束阻抗检测并确认设备已释放。");
    }
    private void SetImpedanceMode(AcquisitionDeviceMode mode, Guid? sessionId, string detail)
    {
        impedanceMode = new(mode, sessionId, detail);
        DesktopLog.Info($"impedance_mode mode={mode} detection_session_id={sessionId:N}");
        DeviceModeChanged?.Invoke(this, DeviceMode);
    }
    private void ForwardImpedance(object? sender, ImpedanceSnapshot snapshot)
    {
        if (ReferenceEquals(sender, impedanceSession) && impedanceMode.Mode == AcquisitionDeviceMode.Impedance &&
            snapshot.DetectionSessionId == impedanceMode.DetectionSessionId)
            ImpedanceUpdated?.Invoke(this, snapshot);
    }
    private void OnImpedanceFailed(object? sender, Exception exception)
    {
        if (ReferenceEquals(sender, impedanceSession))
        {
            DesktopLog.Error($"impedance_failed detection_session_id={impedanceMode.DetectionSessionId:N}", exception);
            SetImpedanceMode(AcquisitionDeviceMode.ImpedanceFaulted, impedanceMode.DetectionSessionId, exception.Message);
        }
    }
}

public sealed class AcquisitionDeviceBusyException(string message) : InvalidOperationException(message);
