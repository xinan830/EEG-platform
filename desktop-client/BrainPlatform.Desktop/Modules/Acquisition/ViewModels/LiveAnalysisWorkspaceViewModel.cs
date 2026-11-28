using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Acquisition.Runtime;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

/// <summary>
/// Owns multiple concurrent live algorithm cards in Acquisition, mirroring Review's MultiAnalysisDockHost workflow.
/// </summary>
public sealed partial class LiveAnalysisWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly ILiveAnalysisBackend backend;
    private readonly Dispatcher dispatcher;
    private readonly Action<Exception> reportError;
    private readonly SemaphoreSlim backendGate = new(1, 1);
    private LiveAnalysisCardViewModel? activeSlot;
    private int busyCount;
    private long montageGeneration;
    private bool disposed;

    public LiveAnalysisWorkspaceViewModel(
        ConfiguredAcquisitionRuntime runtime,
        Dispatcher dispatcher,
        Action<Exception> reportError)
        : this(new RuntimeLiveAnalysisBackend(runtime), dispatcher, reportError)
    {
    }

    public LiveAnalysisWorkspaceViewModel(
        ILiveAnalysisBackend backend,
        Dispatcher dispatcher,
        Action<Exception> reportError)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));

        OpenRbpCommand = new AsyncRelayCommand(() => OpenOrFocusAsync("rbp"), reportError);
        OpenPsdCommand = new AsyncRelayCommand(() => OpenOrFocusAsync("psd"), reportError);
        OpenStftCommand = new AsyncRelayCommand(() => OpenOrFocusAsync("stft"), reportError);
        OpenIapfCommand = new AsyncRelayCommand(() => OpenOrFocusAsync("iapf"), reportError);
        OpenThetaBetaCommand = new AsyncRelayCommand(() => OpenOrFocusAsync("theta_beta"), reportError);
        OpenBrainbeatCommand = new AsyncRelayCommand(() => OpenOrFocusAsync("brainbeat"), reportError);
        FocusSlotCommand = new RelayCommand<LiveAnalysisCardViewModel>(slot => { if (slot is not null) FocusSlot(slot.AlgorithmId); });
        FloatingWindowClosedCommand = new RelayCommand<LiveAnalysisCardViewModel>(slot => { if (slot is not null) HandleFloatingWindowClosedByUser(slot); });
        FloatSlotCommand = new RelayCommand<LiveAnalysisCardViewModel>(slot => { if (slot is { IsFloating: false }) ToggleFloatingSlot(slot.AlgorithmId); });
        DockFloatingSlotCommand = new RelayCommand<LiveAnalysisCardViewModel>(slot => { if (slot is not null) DockFloatingSlotDirect(slot); });

        backend.LiveAnalysisUpdated += OnUpdate;
        backend.LiveAnalysisFaulted += OnFault;
        backend.LiveMontageChanged += OnMontageChanged;
    }

    public bool IsSessionReady => backend.State.State is AcquisitionState.Previewing or AcquisitionState.Recording or AcquisitionState.Paused;
    public bool IsInteractive => IsSessionReady && Volatile.Read(ref busyCount) == 0;

    internal void ReportCommandError(Exception exception) => reportError(exception);

    public void RefreshSession()
    {
        if (!IsSessionReady || (backend.State.SessionId is { } activeSession && OpenSlots.Any(slot => slot.SessionId != activeSession)))
        {
            CloseAllSlotsLocally();
        }
        RaisePropertyChanged(nameof(IsSessionReady));
        RaisePropertyChanged(nameof(IsInteractive));
    }

    public async Task OpenOrFocusAsync(string algorithmId)
    {
        if (disposed || !IsSessionReady) return;
        if (GetOpenSlot(algorithmId) is not null)
        {
            FocusSlot(algorithmId);
            return;
        }
        if (backend.State.SessionId is not { } session || backend.StreamMetadata is null) return;

        var channels = CurrentAnalysisChannels();
        if (channels.Count == 0) throw new InvalidOperationException("当前采集会话没有可用的脑电通道。");

        var slot = CreateCard(algorithmId, session, channels);
        foreach (var right in RightDockedSlots) right.IsMaximized = false;
        OpenSlots.Add(slot);
        RebuildDockCollections();
        SetActiveSlot(slot);

        try
        {
            await ApplyCardConfigurationAsync(slot, forceRestart: true, expectedMontageRevision: null);
        }
        catch
        {
            if (OpenSlots.Remove(slot))
            {
                slot.IsClosed = true;
                RebuildDockCollections();
                if (ReferenceEquals(activeSlot, slot)) SetActiveSlot(OpenSlots.LastOrDefault());
            }
            throw;
        }
    }

    public async Task CloseSlotAsync(string algorithmId)
    {
        if (GetOpenSlot(algorithmId) is not { } slot) return;
        slot.IsClosed = true;
        OpenSlots.Remove(slot);
        RebuildDockCollections();
        if (ReferenceEquals(activeSlot, slot)) SetActiveSlot(OpenSlots.LastOrDefault());

        var idsToStop = slot.ActiveBackendIds;
        slot.ActiveBackendIds = [];
        slot.LastAppliedConfigKey = null;
        if (idsToStop.Count == 0) return;

        await RunBackendTransitionAsync(async () =>
        {
            foreach (var id in idsToStop)
                await backend.SetLiveAlgorithmEnabledAsync(id, false, default, CancellationToken.None);
        });
    }

    private async Task SafeReconfigureAsync(LiveAnalysisCardViewModel card)
    {
        try { await ApplyCardConfigurationAsync(card, forceRestart: false, expectedMontageRevision: null); }
        catch (Exception exception) { reportError(exception); }
    }

    private async Task ApplyCardConfigurationAsync(LiveAnalysisCardViewModel card, bool forceRestart, long? expectedMontageRevision)
    {
        if (disposed || card.IsClosed || !IsSessionReady || backend.State.SessionId is not { } session ||
            backend.StreamMetadata is not { } stream) return;
        if (expectedMontageRevision is { } expected && backend.LiveMontage?.Revision != expected) return;

        var requests = BuildBackendRequests(card, stream);
        var configKey = string.Join("|", requests.Select(r => $"{r.BackendId}:{r.Config.GetRawText()}"));
        if (!forceRestart && string.Equals(card.LastAppliedConfigKey, configKey, StringComparison.Ordinal)) return;

        var previousIds = card.ActiveBackendIds;
        if (requests.Count == 0)
        {
            card.LastAppliedConfigKey = null;
            card.Revision = -1;
            card.SecondaryRevision = -1;
            await RunBackendTransitionAsync(async () =>
            {
                foreach (var oldId in previousIds)
                    await backend.SetLiveAlgorithmEnabledAsync(oldId, false, default, CancellationToken.None);
                card.ActiveBackendIds = [];
            });
            return;
        }
        card.LastAppliedConfigKey = configKey;
        card.Revision = -1;
        card.SecondaryRevision = -1;
        ResetCardPresentationBeforeRun(card, session);

        await RunBackendTransitionAsync(async () =>
        {
            foreach (var oldId in previousIds)
                await backend.SetLiveAlgorithmEnabledAsync(oldId, false, default, CancellationToken.None);

            if (card.IsClosed || backend.State.SessionId != session || !IsSessionReady)
            {
                card.ActiveBackendIds = [];
                return;
            }

            var started = new List<string>(requests.Count);
            try
            {
                for (var index = 0; index < requests.Count; index++)
                {
                    var request = requests[index];
                    try
                    {
                        await backend.SetLiveAlgorithmEnabledAsync(request.BackendId, true, request.Config, CancellationToken.None);
                        started.Add(request.BackendId);
                    }
                    catch (Exception exception) when (index > 0 && request.Optional)
                    {
                        DesktopLog.Warning($"live_optional_algorithm_start_failed id={request.BackendId} {exception.Message}");
                    }
                }
                card.ActiveBackendIds = started;
            }
            catch
            {
                card.ActiveBackendIds = started;
                card.LastAppliedConfigKey = null;
                throw;
            }
        });
    }

    private void OnMontageChanged(LiveMontageSnapshot montage)
    {
        var switchGeneration = Interlocked.Increment(ref montageGeneration);
        _ = dispatcher.InvokeAsync(async () =>
        {
            if (disposed || !IsSessionReady || switchGeneration != Interlocked.Read(ref montageGeneration)) return;
            var nextChannels = LiveSpectralPresentation.AnalysisChannelNames(montage);
            foreach (var card in OpenSlots.ToArray())
            {
                if (card.HasSecondaryChannel)
                {
                    card.Channels = LiveScalarPresentation.BrainbeatChannelOptions(nextChannels, montage, "Fz");
                    card.SecondaryChannels = LiveScalarPresentation.BrainbeatChannelOptions(nextChannels, montage, "Pz");
                    var (nextPrimary, nextSecondary) = LiveScalarPresentation.ResolveBrainbeatChannels(
                        nextChannels, montage, card.SelectedChannel, card.SecondaryChannel);
                    card.SetSelectedChannelSilently(nextPrimary);
                    card.SetSecondaryChannelSilently(nextSecondary);
                }
                else
                {
                    card.Channels = nextChannels;
                    card.SetSelectedChannelSilently(LiveScalarPresentation.ResolveSingleChannel(
                        nextChannels, montage, card.SelectedChannel));
                }
                try { await ApplyCardConfigurationAsync(card, forceRestart: true, expectedMontageRevision: montage.Revision); }
                catch (Exception exception)
                {
                    DesktopLog.Warning($"live_analysis_montage_restart_failed algorithm={card.AlgorithmId} revision={montage.Revision} {exception.Message}");
                }
            }
        });
    }

    private void OnFault(object? sender, AcquisitionFault fault)
    {
        _ = dispatcher.InvokeAsync(() =>
        {
            if (disposed) return;
            foreach (var card in OpenSlots)
            {
                card.ActiveBackendIds = [];
                card.LastAppliedConfigKey = null;
                card.StatusText = fault.Detail;
                card.RbpAnalysis?.Clear(fault.Detail);
                card.PsdAnalysis?.SetFault(fault.Detail);
                card.StftAnalysis?.SetFault(fault.Detail);
                card.ScalarAnalysis?.SetFault(fault.Detail);
            }
        });
    }

    private void OnUpdate(object? sender, LiveAnalysisUpdate update)
    {
        _ = dispatcher.InvokeAsync(() =>
        {
            if (disposed || !IsSessionReady || backend.State.SessionId != update.SessionId) return;
            if (backend.LiveMontage is { } montage && !update.IsCurrentMontageResult(montage)) return;

            var card = FindTargetCard(update.AlgorithmId);
            if (card is null || card.SessionId != update.SessionId) return;

            var isSecondary = update.AlgorithmId == "rbp-topomap";
            var currentRevision = isSecondary ? card.SecondaryRevision : card.Revision;
            if (update.Revision <= currentRevision) return;
            if (isSecondary) card.SecondaryRevision = update.Revision;
            else card.Revision = update.Revision;

            LiveAnalysisCardProjector.Apply(card, update);
        });
    }

    private LiveAnalysisCardViewModel? FindTargetCard(string algorithmId) => algorithmId switch
    {
        "rbp" or "rbp-topomap" => GetOpenSlot("rbp"),
        "psd" => GetOpenSlot("psd") is { PsdAnalysis.IsTopomapSelected: false } card ? card : null,
        "psd-topomap" => GetOpenSlot("psd") is { PsdAnalysis.IsTopomapSelected: true } card ? card : null,
        "stft" => GetOpenSlot("stft"),
        "iapf" or "theta_beta" or "brainbeat" => GetOpenSlot(algorithmId),
        _ => null,
    };

    private IReadOnlyList<string> CurrentAnalysisChannels() => backend.LiveMontage is { } montage
        ? LiveSpectralPresentation.AnalysisChannelNames(montage)
        : backend.StreamMetadata is { } stream
            ? LiveSpectralPresentation.AnalysisChannelNames(stream)
            : [];

    private async Task RunBackendTransitionAsync(Func<Task> action)
    {
        Interlocked.Increment(ref busyCount);
        RaisePropertyChanged(nameof(IsInteractive));
        await backendGate.WaitAsync();
        try { await action(); }
        finally
        {
            backendGate.Release();
            Interlocked.Decrement(ref busyCount);
            RaisePropertyChanged(nameof(IsInteractive));
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        backend.LiveAnalysisUpdated -= OnUpdate;
        backend.LiveAnalysisFaulted -= OnFault;
        backend.LiveMontageChanged -= OnMontageChanged;
        CloseAllSlotsLocally();
        backendGate.Dispose();
    }
}
