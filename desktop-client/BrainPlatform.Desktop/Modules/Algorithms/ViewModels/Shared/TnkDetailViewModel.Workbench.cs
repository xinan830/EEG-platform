using System.Diagnostics;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Diagnostics;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed partial class TnkDetailViewModel
{
    private TnkChildResultDisplay? selectedMetric;
    private TnkChartSeries? selectedMetricSeries;
    private SignalPreviewResponse? waveform;
    private CancellationTokenSource? waveformRequest;
    private long waveformRevision;
    private string waveformStatus = "选择记录后显示波形";
    private double waveformPosition;
    private bool workbenchActive;
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
    public SignalPreviewResponse? Waveform { get => waveform; private set => SetProperty(ref waveform, value); }
    public string WaveformStatus { get => waveformStatus; private set => SetProperty(ref waveformStatus, value); }
    public double RecordingDuration => Catalog.RegisteredRecording?.DurationSeconds ?? 0;
    public double WaveformPosition
    {
        get => waveformPosition;
        set
        {
            hasSelectedTime = true;
            SetProperty(ref waveformPosition, Math.Clamp(value, 0, Math.Max(0, RecordingDuration - .001)));
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
        LoadWaveformCommand = new AsyncRelayCommand(LoadWaveformAsync, error => WaveformStatus = error.Message);
        PreviousWaveformCommand = new RelayCommand(() => SeekFromChart(WaveformPosition - 10));
        NextWaveformCommand = new RelayCommand(() => SeekFromChart(WaveformPosition + 10));
    }

    private void RefreshSelectedMetric()
    {
        var id = SelectedMetric?.AlgorithmId;
        var seriesName = SelectedMetricSeries?.Name;
        SelectedMetric = ChildResults.FirstOrDefault(child => child.AlgorithmId == id) ?? ChildResults.FirstOrDefault();
        SelectedMetricSeries = SelectedMetric?.Chart.Series.FirstOrDefault(series => series.Name == seriesName)
            ?? SelectedMetric?.Chart.Series.FirstOrDefault();
        NotifyPresentation();
    }

    internal void ActivateWorkbench()
    {
        if (workbenchActive) return;
        workbenchActive = true;
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
        DeactivateSidebar();
        workbenchActive = false;
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
                childResults = projected;
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
        waveformRevision++; waveformRequest?.Cancel(); Waveform = null;
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

    private void ReloadWaveform()
    {
        if (workbenchActive) _ = LoadWaveformSafelyAsync();
    }
    private async Task LoadWaveformSafelyAsync()
    {
        try { await LoadWaveformAsync(); } catch (Exception error)
        {
            if (workbenchActive)
            {
                WaveformStatus = error.Message;
                StopWorkbenchPlayback();
            }
        }
    }

    private async Task LoadWaveformAsync()
    {
        var revision = ++waveformRevision;
        waveformRequest?.Cancel(); waveformRequest?.Dispose();
        using var request = new CancellationTokenSource(); waveformRequest = request;
        try
        {
            WaveformStatus = "正在读取当前导联波形…";
            var result = await Catalog.LoadSignalPreviewAsync(Math.Max(0, WaveformPosition - 10), request.Token);
            if (revision != waveformRevision || request.IsCancellationRequested) return;
            if (result.RecordingId != Catalog.RegisteredRecording?.Id || result.MontageId != Catalog.SelectedMontageProfile?.Id)
                throw new InvalidOperationException("波形来源身份已变化，拒绝绑定。");
            Waveform = result; WaveformStatus = $"{result.LowHz:G}–{result.HighHz:G} Hz · 陷波 {(result.NotchHz is double notch ? notch + " Hz" : "关闭")} · 连续零相位滤波 · 原始记录不变";
        }
        catch (OperationCanceledException) { }
        catch (Exception) when (revision != waveformRevision || request.IsCancellationRequested) { }
        finally { if (ReferenceEquals(waveformRequest, request)) waveformRequest = null; }
    }

    internal void SeekFromChart(double seconds)
    {
        WaveformPosition = seconds;
        playbackOrigin = WaveformPosition; playbackClock.Restart();
        ReloadWaveform();
        if (Catalog.IsOnDemandAnalysis) Catalog.SeekDynamicPreviewSecondsForModule(WaveformPosition);
    }

    internal void PauseForWaveformSeek() => StopWorkbenchPlayback();
}
