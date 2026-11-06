using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Modules.Algorithms.Diagnostics;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed partial class RecordingReviewViewModel
{
    private CancellationTokenSource? customFrequencyBinsCancellation;
    private CancellationTokenSource? customFrequencyRangeCancellation;
    private WindowPreviewResponse? customFrequencyPsdWindow;
    private WindowPreviewResponse? customFrequencyRbpWindow;
    private long customFrequencyWindowRevision;
    private long customFrequencyRangeRevision;
    private bool customFrequencyBinsLoading;

    public ReviewCustomFrequencyInspectionViewModel CustomFrequencyInspection { get; } = new();
    public ICommand OpenCustomFrequencyInspectionCommand { get; private set; } = null!;
    public bool IsCustomFrequencyInspectionPanelOpen => activeAnalysisId == CustomFrequencyInspectionPanelId;

    private void InitializeCustomFrequencyInspection()
    {
        OpenCustomFrequencyInspectionCommand =
            new AsyncRelayCommand(() => OpenAnalysisAsync(CustomFrequencyInspectionPanelId), ReportCommandError);
        CustomFrequencyInspection.PropertyChanged += OnCustomFrequencyInspectionChanged;
    }

    private void OnCustomFrequencyInspectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsCustomFrequencyInspectionPanelOpen) return;
        if (e.PropertyName is nameof(ReviewCustomFrequencyInspectionViewModel.RangeLowHz) or
            nameof(ReviewCustomFrequencyInspectionViewModel.RangeHighHz) or
            nameof(ReviewCustomFrequencyInspectionViewModel.Mode))
            _ = ScheduleCustomFrequencyRangeProjection();
    }

    private async Task RefreshCustomFrequencyInspectionAsync()
    {
        if (!IsCustomFrequencyInspectionPanelOpen) return;
        var psd = analysisBinding?.CurrentWindow;
        var rbp = spectralRbpBinding?.CurrentWindow;
        if (isTimelineDragging || psd is null || rbp is null)
        {
            CancelCustomFrequencyProjection();
            CustomFrequencyInspection.Clear($"PSD：{analysisBinding?.StatusText ?? "准备中"}；RBP：{spectralRbpBinding?.StatusText ?? "准备中"}");
            return;
        }
        if (ReferenceEquals(psd, customFrequencyPsdWindow) &&
            ReferenceEquals(rbp, customFrequencyRbpWindow) &&
            (CustomFrequencyInspection.HasBins || customFrequencyBinsLoading)) return;

        CancelCustomFrequencyProjection();
        customFrequencyPsdWindow = psd;
        customFrequencyRbpWindow = rbp;
        customFrequencyBinsLoading = true;
        var revision = customFrequencyWindowRevision;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(analysisLifetime.Token);
        customFrequencyBinsCancellation = request;
        try
        {
            ReviewSpectralSources.Validate(psd, rbp, PsdParameters.Channels);
            if (CustomFrequencyInspection.HasBins) CustomFrequencyInspection.SetPendingWindow(psd.CursorSeconds);
            else CustomFrequencyInspection.Clear("正在读取当前 PSD/RBP 频率网格…");
            var provider = analysisCoordinators["psd"].GetCapability<IReviewCustomFrequencyProvider>() ??
                throw new InvalidOperationException("PSD adapter 不提供自定义频率投影。");
            var fetchStarted = Stopwatch.GetTimestamp();
            var (values, completedAt) = await AlgorithmWindowPerformanceDiagnostics.WithCompletionTimeAsync(
                provider.LoadBinsAsync(psd, rbp, request.Token));
            var continuationMs = Stopwatch.GetElapsedTime(completedAt).TotalMilliseconds;
            if (IsCurrentCustomFrequencyWindow(revision, psd, rbp) && !request.IsCancellationRequested)
            {
                var applyStarted = Stopwatch.GetTimestamp();
                CustomFrequencyInspection.ApplyBins(psd, rbp, values);
                RecordCustomFrequencyApply("bins", psd, rbp, fetchStarted, continuationMs, applyStarted);
                customFrequencyBinsLoading = false;
                if (CustomFrequencyInspection.Mode == ReviewCustomFrequencyMode.Range)
                    await ScheduleCustomFrequencyRangeProjection(debounce: false);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrentCustomFrequencyWindow(revision, psd, rbp) && !request.IsCancellationRequested)
            {
                CustomFrequencyInspection.Clear($"频率数值不可用：{exception.Message}");
                customFrequencyPsdWindow = customFrequencyRbpWindow = null;
            }
        }
        finally
        {
            if (ReferenceEquals(customFrequencyBinsCancellation, request))
            {
                customFrequencyBinsCancellation = null;
                customFrequencyBinsLoading = false;
            }
        }
    }

    private Task ScheduleCustomFrequencyRangeProjection(bool debounce = true)
    {
        CancelCustomFrequencyRangeProjection();
        if (!IsCustomFrequencyInspectionPanelOpen ||
            CustomFrequencyInspection.Mode != ReviewCustomFrequencyMode.Range ||
            !CustomFrequencyInspection.HasBins || customFrequencyBinsLoading) return Task.CompletedTask;
        if (!CustomFrequencyInspection.IsRangeValid)
        {
            CustomFrequencyInspection.ClearRangeProjection(
                $"范围需满足 {CustomFrequencyInspection.MinimumFrequencyHz:0.###} ≤ 起点 < 终点 ≤ {CustomFrequencyInspection.MaximumFrequencyHz:0.###} Hz。");
            return Task.CompletedTask;
        }

        var psd = customFrequencyPsdWindow;
        var rbp = customFrequencyRbpWindow;
        if (psd is null || rbp is null || !IsCurrentCustomFrequencyWindow(customFrequencyWindowRevision, psd, rbp)) return Task.CompletedTask;
        var lowHz = CustomFrequencyInspection.RangeLowHz;
        var highHz = CustomFrequencyInspection.RangeHighHz;
        var windowRevision = customFrequencyWindowRevision;
        var rangeRevision = customFrequencyRangeRevision;
        var request = CancellationTokenSource.CreateLinkedTokenSource(analysisLifetime.Token);
        customFrequencyRangeCancellation = request;
        return LoadCustomFrequencyRangeAsync(psd, rbp, lowHz, highHz, windowRevision, rangeRevision, request, debounce);
    }

    private async Task LoadCustomFrequencyRangeAsync(WindowPreviewResponse psd, WindowPreviewResponse rbp,
        double lowHz, double highHz, long windowRevision, long rangeRevision, CancellationTokenSource request, bool debounce)
    {
        try
        {
            CustomFrequencyInspection.ClearRangeProjection("正在投影当前自定义范围…");
            if (debounce) await Task.Delay(180, request.Token);
            var provider = analysisCoordinators["psd"].GetCapability<IReviewCustomFrequencyProvider>() ??
                throw new InvalidOperationException("PSD adapter 不提供自定义频率投影。");
            var fetchStarted = Stopwatch.GetTimestamp();
            var (values, completedAt) = await AlgorithmWindowPerformanceDiagnostics.WithCompletionTimeAsync(
                provider.LoadRangeAsync(psd, rbp, lowHz, highHz, request.Token));
            var continuationMs = Stopwatch.GetElapsedTime(completedAt).TotalMilliseconds;
            if (IsCurrentCustomFrequencyRange(windowRevision, rangeRevision, psd, rbp, lowHz, highHz) &&
                !request.IsCancellationRequested)
            {
                var applyStarted = Stopwatch.GetTimestamp();
                CustomFrequencyInspection.ApplyRange(values);
                RecordCustomFrequencyApply("range", psd, rbp, fetchStarted, continuationMs, applyStarted);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrentCustomFrequencyRange(windowRevision, rangeRevision, psd, rbp, lowHz, highHz) &&
                !request.IsCancellationRequested)
                CustomFrequencyInspection.ClearRangeProjection($"范围数值不可用：{exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(customFrequencyRangeCancellation, request)) customFrequencyRangeCancellation = null;
            request.Dispose();
        }
    }

    private bool IsCurrentCustomFrequencyWindow(long revision, WindowPreviewResponse psd, WindowPreviewResponse rbp) =>
        IsCustomFrequencyInspectionPanelOpen && !isTimelineDragging && revision == customFrequencyWindowRevision &&
        ReferenceEquals(psd, customFrequencyPsdWindow) && ReferenceEquals(rbp, customFrequencyRbpWindow);

    private bool IsCurrentCustomFrequencyRange(long windowRevision, long rangeRevision,
        WindowPreviewResponse psd, WindowPreviewResponse rbp, double lowHz, double highHz) =>
        rangeRevision == customFrequencyRangeRevision && IsCurrentCustomFrequencyWindow(windowRevision, psd, rbp) &&
        CustomFrequencyInspection.Mode == ReviewCustomFrequencyMode.Range &&
        CustomFrequencyInspection.RangeLowHz == lowHz && CustomFrequencyInspection.RangeHighHz == highHz;

    private void CancelCustomFrequencyProjection()
    {
        customFrequencyWindowRevision++;
        customFrequencyBinsCancellation?.Cancel();
        customFrequencyBinsCancellation = null;
        customFrequencyBinsLoading = false;
        CancelCustomFrequencyRangeProjection();
        customFrequencyPsdWindow = customFrequencyRbpWindow = null;
    }

    private void CancelCustomFrequencyRangeProjection()
    {
        customFrequencyRangeRevision++;
        customFrequencyRangeCancellation?.Cancel();
        customFrequencyRangeCancellation = null;
    }

    private void RecordCustomFrequencyApply(string mode, WindowPreviewResponse psd, WindowPreviewResponse rbp,
        long fetchStarted, double continuationMs, long applyStarted)
    {
        if (!DesktopLog.PerformanceDiagnosticsEnabled) return;
        DesktopLog.Info($"review_custom_frequency_apply_perf review_session_id={ReviewSessionId:N} recording_id={recordingId}" +
            $" mode={mode} psd_window_id={psd.Window?.WindowId} rbp_window_id={rbp.Window?.WindowId}" +
            $" continuation_delay_ms={continuationMs:0.000} apply_ms={Stopwatch.GetElapsedTime(applyStarted).TotalMilliseconds:0.000}" +
            $" fetch_to_apply_ms={Stopwatch.GetElapsedTime(fetchStarted).TotalMilliseconds:0.000}" +
            $" window_start_s={FormatPerformanceSeconds(psd.WindowStartSeconds)} window_end_s={FormatPerformanceSeconds(psd.WindowEndSeconds)}" +
            $" playback_position_s={PositionSeconds:0.000} window_lag_s={FormatPerformanceSeconds(PositionSeconds - psd.WindowEndSeconds)}" +
            $" rows={CustomFrequencyInspection.Rows.Count} bins={CustomFrequencyInspection.FrequenciesHz.Count}");
    }
}
