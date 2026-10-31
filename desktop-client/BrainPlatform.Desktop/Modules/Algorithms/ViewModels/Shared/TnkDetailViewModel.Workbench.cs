using System.Diagnostics;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Diagnostics;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed partial class TnkDetailViewModel
{
    private TnkChildResultDisplay? selectedMetric;
    private TnkChartSeries? selectedMetricSeries;
    private double waveformPosition;
    private bool workbenchActive;
    private TnkDispatcherOperationDiagnostics? dispatcherOperationDiagnostics;
    private bool historyRefreshQueued;
    private readonly object historyProjectionGate = new();
    private IReadOnlyList<TnkChildResultDisplay>? pendingHistorySource;
    private string pendingHistoryWindowId = "unknown";
    private long? pendingHistoryWindowEndSample;
    private long historyProjectionRevision;
    private readonly HashSet<TnkRoleChannelSelection> observedRoles = [];
    private bool hasSelectedTime;
    private string? withheldRunId;
    private double waveformLaneMicrovolts = 200;
    public IReadOnlyList<double> WaveformScales { get; } = [100, 200, 400, 800];
    public double WaveformLaneMicrovolts
    {
        get => waveformLaneMicrovolts;
        set { if (WaveformScales.Contains(value)) SetProperty(ref waveformLaneMicrovolts, value); }
    }
    public TnkChildResultDisplay? SelectedMetric
    {
        get => selectedMetric;
        set
        {
            if (!SetProperty(ref selectedMetric, value)) return;
            SelectedMetricSeries = value?.Chart.Series.FirstOrDefault();
            RaisePropertyChanged(nameof(SelectedValueText));
            NotifyCursorPresentation();
            RefreshScoreStates();
        }
    }
    public TnkChartSeries? SelectedMetricSeries
    {
        get => selectedMetricSeries;
        set { if (SetProperty(ref selectedMetricSeries, value)) RaisePropertyChanged(nameof(SelectedValueText)); }
    }
    private TnkChartPoint? SelectedPoint => hasSelectedTime && SelectedMetric?.Chart.IsTimeline == true
        ? SelectedMetricSeries?.Points.LastOrDefault(point => point.X <= WaveformPosition)
        : SelectedMetricSeries?.Points.LastOrDefault();
    public string SelectedValueText => SelectedPoint is { } point
        ? point.Value is double value ? $"{value:G6} {SelectedMetricSeries!.Unit} · {point.Label}" : "当前窗口不可用"
        : hasSelectedTime ? "此时间尚无完整计算窗口" : "等待结果";
    public string IapfUpdateStatus
    {
        get
        {
            if (!Catalog.IsOnDemandAnalysis) return "";
            var child = ChildResults.FirstOrDefault(item => item.AlgorithmId == "tnk_iapf");
            if (child?.FailureCode == "TNK_UPDATE_NOT_DUE")
            {
                var last = child.Chart.Series.SelectMany(series => series.Points)
                    .LastOrDefault(point => point.Value.HasValue && point.X <= WaveformPosition);
                return last is null
                    ? "等待 30 秒预热与首个 5 秒更新节拍"
                    : $"最近计算于 {last.X:0.#} s · 当前窗口未到 5 秒更新节拍";
            }
            return child?.FailureText ?? child?.QualityText ?? "等待当前窗口结果";
        }
    }
    public double RecordingDuration => Catalog.RegisteredRecording?.DurationSeconds ?? 0;
    public double WaveformPosition
    {
        get => waveformPosition;
        set
        {
            var previousSecond = (long)Math.Floor(waveformPosition);
            hasSelectedTime = true;
            if (!SetProperty(ref waveformPosition, Math.Clamp(value, 0, Math.Max(0, RecordingDuration - .001)))) return;
            if (Playback?.IsPlaying == true && previousSecond == (long)Math.Floor(waveformPosition)) return;

            RaisePropertyChanged(nameof(SelectedValueText));
            NotifyCursorPresentation();
            RefreshScoreStates();
        }
    }
    public ICommand LoadWaveformCommand { get; private set; } = null!;
    public ICommand PreviousWaveformCommand { get; private set; } = null!;
    public ICommand NextWaveformCommand { get; private set; } = null!;

    private void InitializeWorkbench()
    {
        InitializePlayback();
        InitializeWaveformCommands();
    }

    private void RefreshSelectedMetric()
    {
        var id = SelectedMetric?.AlgorithmId;
        var seriesName = SelectedMetricSeries?.Name;
        var nextMetric = ChildResults.FirstOrDefault(child => child.AlgorithmId == id) ?? ChildResults.FirstOrDefault();
        selectedMetric = nextMetric;
        selectedMetricSeries = nextMetric?.Chart.Series.FirstOrDefault(series => series.Name == seriesName)
            ?? nextMetric?.Chart.Series.FirstOrDefault();
        RaisePropertyChanged(nameof(SelectedMetric));
        RaisePropertyChanged(nameof(SelectedMetricSeries));
        RaisePropertyChanged(nameof(SelectedValueText));
        NotifyPresentation();
    }

    internal void ActivateWorkbench()
    {
        if (workbenchActive) return;
        workbenchActive = true;
        if (DesktopLog.PerformanceDiagnosticsEnabled && System.Windows.Application.Current is { } application)
            dispatcherOperationDiagnostics = new TnkDispatcherOperationDiagnostics(application.Dispatcher);
        System.Windows.Media.CompositionTarget.Rendering += OnPlaybackRendering;
        Catalog.OwnedWindowCompleted += OnOwnedWindowCompleted;
        Catalog.OwnedTnkPlaybackGap += OnOwnedTnkPlaybackGap;
        Catalog.OwnedTnkPlaybackRecoveryFailed += OnOwnedTnkPlaybackRecoveryFailed;
        Catalog.TnkRoleChannels.CollectionChanged += OnRolesChanged;
        ObserveRoles();
        RefreshSelectedMetric();
        ActivateSidebar();
        LoadWaveformCommand.Execute(null);
    }

    internal void DeactivateWorkbench()
    {
        StopWorkbenchPlayback();
        System.Windows.Media.CompositionTarget.Rendering -= OnPlaybackRendering;
        DeactivateSidebar();
        workbenchActive = false;
        dispatcherOperationDiagnostics?.Dispose();
        dispatcherOperationDiagnostics = null;
        historyRefreshQueued = false;
        lock (historyProjectionGate)
        {
            pendingHistorySource = null;
            pendingHistoryWindowId = "unknown";
            pendingHistoryWindowEndSample = null;
            historyProjectionRevision++;
        }
        Catalog.OwnedWindowCompleted -= OnOwnedWindowCompleted;
        Catalog.OwnedTnkPlaybackGap -= OnOwnedTnkPlaybackGap;
        Catalog.OwnedTnkPlaybackRecoveryFailed -= OnOwnedTnkPlaybackRecoveryFailed;
        Catalog.TnkRoleChannels.CollectionChanged -= OnRolesChanged;
        foreach (var role in observedRoles) role.PropertyChanged -= OnRoleChanged;
        observedRoles.Clear();
        waveformRevision++; waveformRequest?.Cancel(); waveformRequest?.Dispose(); waveformRequest = null;
        waveformReloadPending = false;
    }

    private void OnOwnedTnkPlaybackGap(TnkPlaybackGap gap)
    {
        if (!workbenchActive || !Catalog.IsOnDemandAnalysis || Algorithm is null ||
            !string.Equals(Catalog.ActivePlaybackDeliveryId, gap.DeliveryId, StringComparison.Ordinal)) return;
        windowHistory.AddGap(gap);
        windowHistory.BeginReconciliation();
        QueueHistoryProjection(childResults ?? [], "gap", gap.AffectedEndSample);
    }

    private void OnOwnedTnkPlaybackRecoveryFailed(string deliveryId)
    {
        if (!workbenchActive || !Catalog.IsOnDemandAnalysis ||
            !string.Equals(Catalog.ActivePlaybackDeliveryId, deliveryId, StringComparison.Ordinal)) return;
        windowHistory.MarkReconciliationFailed();
        RaisePropertyChanged(nameof(HistoryCoverageText));
    }

    private void OnOwnedWindowCompleted(DynamicWindowRequest request, DynamicWindowResponse response)
    {
        if (!workbenchActive || !Catalog.IsOnDemandAnalysis ||
            !string.Equals(request.AlgorithmId, "tnk_biofeedback", StringComparison.OrdinalIgnoreCase) ||
            !Catalog.IsCurrentDynamicRequest(request) || Algorithm is null)
            return;

        var returned = TnkResultChartProjection.ReadWindow(response, Algorithm.OutputSchema);
        if (!windowHistory.Add(response, returned)) return;
        QueueHistoryProjection(returned, response.WindowId, response.Range?.EndSample);
    }

    private void QueueHistoryProjection(IReadOnlyList<TnkChildResultDisplay> source,
        string windowId, long? windowEndSample)
    {
        lock (historyProjectionGate)
        {
            pendingHistorySource = source;
            pendingHistoryWindowId = windowId;
            pendingHistoryWindowEndSample = windowEndSample;
            historyProjectionRevision++;
            if (historyRefreshQueued) return;
            historyRefreshQueued = true;
        }
        _ = DrainHistoryProjectionAsync();
    }

    private async Task DrainHistoryProjectionAsync()
    {
        while (true)
        {
            IReadOnlyList<TnkChildResultDisplay>? source;
            string sourceWindowId;
            long? sourceWindowEndSample;
            long revision;
            lock (historyProjectionGate)
            {
                source = pendingHistorySource;
                pendingHistorySource = null;
                sourceWindowId = pendingHistoryWindowId;
                sourceWindowEndSample = pendingHistoryWindowEndSample;
                pendingHistoryWindowId = "unknown";
                pendingHistoryWindowEndSample = null;
                revision = historyProjectionRevision;
                if (source is null)
                {
                    historyRefreshQueued = false;
                    return;
                }
            }
            var projectionStarted = Stopwatch.GetTimestamp();
            var projected = await Task.Run(() => windowHistory.Project(source,
                Catalog.SelectedDynamicStepSeconds)).ConfigureAwait(false);
            var projectionElapsed = Stopwatch.GetElapsedTime(projectionStarted).TotalMilliseconds;
            if (AlgorithmWindowPerformanceDiagnostics.Enabled)
                DesktopLog.Info($"tnk_playback_history_projection window_id={sourceWindowId}" +
                    $" window_end_sample={sourceWindowEndSample?.ToString() ?? "unknown"}" +
                    $" source_metrics={source.Count} projected_metrics={projected.Count}" +
                    $" elapsed_ms={projectionElapsed:0.0}");
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                lock (historyProjectionGate) historyRefreshQueued = false;
                return;
            }
            var dispatcherQueuedAt = Stopwatch.GetTimestamp();
            _ = dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() =>
            {
                lock (historyProjectionGate)
                    if (revision != historyProjectionRevision) return;
                if (!workbenchActive) return;
                var applyStarted = Stopwatch.GetTimestamp();
                var previousCards = childResults;
                childResults = projected;
                if (!HasSameSidebarCards(previousCards, projected))
                    RaisePropertyChanged(nameof(ChildResults));
                RaisePropertyChanged(nameof(LatestAnalysisWindowText));
                RaisePropertyChanged(nameof(AnalysisLagText));
                RaisePropertyChanged(nameof(HistoryCoverageText));
                RefreshSelectedMetric();
                RefreshScoreStates();
                if (AlgorithmWindowPerformanceDiagnostics.Enabled)
                    DesktopLog.Info($"tnk_playback_history_applied window_id={sourceWindowId}" +
                        $" window_end_sample={sourceWindowEndSample?.ToString() ?? "unknown"}" +
                        $" dispatcher_wait_ms={Stopwatch.GetElapsedTime(dispatcherQueuedAt).TotalMilliseconds:0.0}" +
                        $" apply_ms={Stopwatch.GetElapsedTime(applyStarted).TotalMilliseconds:0.0}" +
                        $" total_ms={Stopwatch.GetElapsedTime(projectionStarted).TotalMilliseconds:0.0}");
            }));
        }
    }

    private static bool HasSameSidebarCards(
        IReadOnlyList<TnkChildResultDisplay>? previous,
        IReadOnlyList<TnkChildResultDisplay> next) =>
        previous is not null &&
        previous.Count == next.Count &&
        previous.Zip(next).All(pair =>
            pair.First.AlgorithmId == pair.Second.AlgorithmId &&
            pair.First.DisplayName == pair.Second.DisplayName &&
            pair.First.QualityText == pair.Second.QualityText &&
            pair.First.FailureText == pair.Second.FailureText);

    private void OnRolesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        ObserveRoles();
        InvalidateWaveformSource(invalidateResults: true);
    }

    private void ObserveRoles()
    {
        foreach (var role in observedRoles) role.PropertyChanged -= OnRoleChanged;
        observedRoles.Clear();
        foreach (var role in Catalog.TnkRoleChannels)
        {
            observedRoles.Add(role);
            role.PropertyChanged += OnRoleChanged;
        }
    }

    private void OnRoleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TnkRoleChannelSelection.SelectedChannel)) InvalidateWaveformSource(invalidateResults: true);
    }

    private void HandleWaveformContextChange(string? property)
    {
        if (property is nameof(AlgorithmListViewModel.RegisteredRecording) or nameof(AlgorithmListViewModel.SelectedMontageProfile)
            or nameof(AlgorithmListViewModel.LowFrequencyText) or nameof(AlgorithmListViewModel.HighFrequencyText)
            or nameof(AlgorithmListViewModel.SelectedNotchFrequency))
        {
            if (property == nameof(AlgorithmListViewModel.RegisteredRecording)) StopWorkbenchPlayback();
            if (property == nameof(AlgorithmListViewModel.RegisteredRecording) && workbenchActive) ActivateSidebar();
            RaisePropertyChanged(nameof(RecordingDuration));
            WaveformPosition = WaveformPosition;
            InvalidateWaveformSource(property is nameof(AlgorithmListViewModel.RegisteredRecording) or nameof(AlgorithmListViewModel.SelectedMontageProfile));
        }
    }

    private void InvalidateWaveformSource(bool invalidateResults = false)
    {
        // Let the current request finish and discard its result by revision. Cancelling
        // here followed by an immediate replacement caused overlapping server-side
        // preview work because the HTTP cancellation did not stop backend processing.
        InvalidateWaveformFrame();
        RaisePropertyChanged(nameof(RoleMappingText));
        if (invalidateResults)
        {
            windowHistory.Clear();
            withheldRunId = Catalog.LastRun?.RunId;
            childResults = null; RaisePropertyChanged(nameof(ChildResults)); RefreshSelectedMetric();
            scoreHistory = null; summaryTarget = ""; summaryRequest?.Cancel();
            ScoreStates = []; RaisePropertyChanged(nameof(ScoreStates));
        }
        ReloadWaveform();
    }

    internal void SeekFromChart(double seconds)
    {
        Playback?.SeekSeconds(seconds);
        WaveformPosition = Playback?.PositionSeconds ?? seconds;
        ReloadWaveform();
        if (Catalog.IsOnDemandAnalysis) Catalog.SeekDynamicPreviewSecondsForModule(WaveformPosition);
    }

    internal void PauseForWaveformSeek() => StopWorkbenchPlayback();
}
