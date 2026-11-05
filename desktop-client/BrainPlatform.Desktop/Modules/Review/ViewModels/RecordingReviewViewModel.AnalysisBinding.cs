using System.Diagnostics;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Review cursor binding and presentation ownership, shared by panel algorithms.</summary>
public sealed partial class RecordingReviewViewModel
{
    private CancellationTokenSource? topomapCancellation;
    private long topomapRequestRevision;
    public async Task AttachAnalysisAsync(string runId, CancellationToken cancellationToken = default)
    {
        var coordinator = analysisCoordinator ?? throw new InvalidOperationException("当前回溯页未配置分析 provider。");
        await DetachAnalysisAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var binding = CreateAnalysisBinding(coordinator);
        binding.AttachRun(runId);
        await LoadAttachedAnalysisAsync(binding, cancellationToken);
    }

    public async Task AttachOnDemandAnalysisAsync(AnalysisRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var coordinator = analysisCoordinator;
        if (coordinator is null || !coordinator.SupportsOnDemandWindows)
            throw new InvalidOperationException("当前回溯页未启用按需动态窗口。");
        var algorithm = await coordinator.GetAlgorithmAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var binding = CreateAnalysisBinding(coordinator);
        binding.AttachOnDemand(request, algorithm);
        await LoadAttachedAnalysisAsync(binding, cancellationToken);
    }

    private ReviewAnalysisBinding CreateAnalysisBinding(ReviewAnalysisCoordinator coordinator)
    {
        var binding = new ReviewAnalysisBinding(coordinator, analysisRecording?.Id ?? recordingId,
            samplingRateHz, checked((long)Math.Round(DurationSeconds * samplingRateHz,
                MidpointRounding.AwayFromZero)), analysisRecording);
        analysisBinding = binding;
        binding.PropertyChanged += OnAnalysisBindingChanged;
        return binding;
    }

    private async Task LoadAttachedAnalysisAsync(ReviewAnalysisBinding binding, CancellationToken cancellationToken)
    {
        isAnalysisStale = false;
        RaiseAnalysisPropertiesChanged();
        try { await binding.LoadAtSecondsAsync(PositionSeconds, cancellationToken); }
        catch
        {
            if (ReferenceEquals(binding, analysisBinding)) await DetachAnalysisAsync();
            throw;
        }
        RaiseAnalysisPropertiesChanged();
    }

    public async ValueTask DetachAnalysisAsync()
    {
        UnsubscribeTnkPlaybackEvents();
        Interlocked.Increment(ref topomapRequestRevision);
        topomapCancellation?.Cancel();
        topomapCancellation?.Dispose();
        topomapCancellation = null;
        RbpAnalysis.ClearTopomap();
        PsdAnalysis.ClearTopomap();
        var binding = analysisBinding;
        if (binding is null) return;
        analysisBinding = null;
        binding.PropertyChanged -= OnAnalysisBindingChanged;
        foreach (var panel in analysisPanels.Values) panel.Apply(null, PositionSeconds);
        RaiseAnalysisPropertiesChanged();
        await binding.DisposeAsync();
    }

    public Task LoadAnalysisAtCurrentPositionAsync() =>
        analysisBinding?.LoadAtSecondsAsync(PositionSeconds) ?? Task.CompletedTask;

    private void OnAnalysisBindingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, analysisBinding)) return;
        if (e.PropertyName == nameof(ReviewAnalysisBinding.CurrentWindow))
        {
            if (activeAnalysisId is { } algorithmId && analysisPanels.TryGetValue(algorithmId, out var panel))
                panel.Apply(CurrentAnalysisWindow, PositionSeconds);
            if (activeAnalysisId == "rbp" && RbpAnalysis.IsTopomapSelected)
                _ = RefreshRbpTopomapAsync();
            if (activeAnalysisId == "psd" && PsdAnalysis.IsTopomapSelected)
                _ = RefreshPsdTopomapAsync();
        }
        if (e.PropertyName == nameof(ReviewAnalysisBinding.StatusText))
            AnalysisPreparationText = CurrentAnalysisWindow is null ? AnalysisStatusText : "";
        RaiseAnalysisPropertiesChanged();
    }

    private async Task RefreshRbpTopomapAsync()
    {
        topomapCancellation?.Cancel();
        topomapCancellation?.Dispose();
        var requestRevision = Interlocked.Increment(ref topomapRequestRevision);
        topomapCancellation = new CancellationTokenSource();
        var token = topomapCancellation.Token;
        var coordinator = analysisCoordinator;
        var window = CurrentAnalysisWindow;
        var provider = coordinator?.GetCapability<IReviewTopomapProvider>();
        if (window is null || coordinator is null || provider is null)
        {
            RbpAnalysis.ClearTopomap();
            return;
        }
        try
        {
            var loadTimer = Stopwatch.StartNew();
            var fields = string.IsNullOrEmpty(RbpAnalysis.SelectedTopomapBandId)
                ? new[] { "delta", "theta", "alpha", "beta", "gamma" }
                    .Select(band => $"rbp.{RbpAnalysis.SelectedTopomapPowerType}.{band}").ToArray()
                : new[] { $"rbp.{RbpAnalysis.SelectedTopomapPowerType}.{RbpAnalysis.SelectedTopomapBandId}" };
            var result = await provider.LoadTopomapAsync(window, token, fields);
            if (!token.IsCancellationRequested && requestRevision == Volatile.Read(ref topomapRequestRevision) &&
                ReferenceEquals(coordinator, analysisCoordinator) && ReferenceEquals(window, CurrentAnalysisWindow) &&
                RbpAnalysis.ApplyTopomapIfAvailable(result))
            {
                var bindTimer = Stopwatch.StartNew();
                if (DesktopLog.PerformanceDiagnosticsEnabled)
                {
                    var gridCells = result?.Fields.Sum(field =>
                        (long)(field.Grid?.Width ?? 0) * (field.Grid?.Height ?? 0)) ?? 0;
                    DesktopLog.Info(
                        $"review_topomap_perf stage=bind" +
                        $" window_id={result?.RunId ?? "none"}" +
                        $" cursor_sample={window.CursorSample}" +
                        $" fields={result?.Fields.Count ?? 0}" +
                        $" grid_cells={gridCells}" +
                        $" load_ms={loadTimer.Elapsed.TotalMilliseconds:0.0}" +
                        $" bind_ms={bindTimer.Elapsed.TotalMilliseconds:0.0}");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            RbpAnalysis.SetStatus($"地形图读取失败：{exception.Message}");
            RbpAnalysis.ClearTopomap();
        }
    }

    private void OnRbpTopomapPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (activeAnalysisId == "rbp" && e.PropertyName is nameof(RbpReviewAnalysisViewModel.SelectedChart) or
                nameof(RbpReviewAnalysisViewModel.SelectedTopomapPowerType) or
                nameof(RbpReviewAnalysisViewModel.SelectedTopomapBandId))
        {
            if (RbpAnalysis.IsTopomapSelected) _ = RefreshRbpTopomapAsync();
            else
            {
                Interlocked.Increment(ref topomapRequestRevision);
                topomapCancellation?.Cancel();
            }
        }
    }

    private async Task RefreshPsdTopomapAsync()
    {
        topomapCancellation?.Cancel();
        topomapCancellation?.Dispose();
        var requestRevision = Interlocked.Increment(ref topomapRequestRevision);
        topomapCancellation = new CancellationTokenSource();
        var token = topomapCancellation.Token;
        var coordinator = analysisCoordinator;
        var window = CurrentAnalysisWindow;
        var provider = coordinator?.GetCapability<IReviewTopomapProvider>();
        if (window is null || coordinator is null || provider is null)
        {
            PsdAnalysis.ClearTopomap();
            return;
        }
        try
        {
            var fieldIds = new[] { PsdAnalysis.SelectedTopomapFieldId };
            var result = await provider.LoadTopomapAsync(window, token, fieldIds);
            if (!token.IsCancellationRequested && requestRevision == Volatile.Read(ref topomapRequestRevision) &&
                ReferenceEquals(coordinator, analysisCoordinator) && ReferenceEquals(window, CurrentAnalysisWindow))
                PsdAnalysis.ApplyTopomap(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            PsdAnalysis.SetStatus($"PSD 地形图读取失败：{exception.Message}");
            PsdAnalysis.ClearTopomap();
        }
    }

    private void CancelTopomapRequest()
    {
        Interlocked.Increment(ref topomapRequestRevision);
        topomapCancellation?.Cancel();
        PsdAnalysis.ClearTopomap();
    }

    private void RaiseAnalysisPropertiesChanged()
    {
        RaisePropertyChanged(nameof(CurrentAnalysisWindow));
        RaisePropertyChanged(nameof(TargetAnalysisCursorSample));
        RaisePropertyChanged(nameof(DisplayedAnalysisCursorSample));
        RaisePropertyChanged(nameof(AnalysisWindowState));
        RaisePropertyChanged(nameof(IsAnalysisTargetDisplayed));
        RaisePropertyChanged(nameof(IsAnalysisLoading));
        RaisePropertyChanged(nameof(AnalysisStatusText));
        RaisePropertyChanged(nameof(IsAnalysisAttached));
        RaisePropertyChanged(nameof(ActiveAnalysisPanel));
        RaisePropertyChanged(nameof(ActiveAnalysisPanelKind));
        RaisePropertyChanged(nameof(IsPsdAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsFaaAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsIapfAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsThetaBetaAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsBrainbeatAnalysisPanelOpen));
        RaisePropertyChanged(nameof(IsTnkAnalysisPanelOpen));
        RaisePropertyChanged(nameof(ActiveScalarParameters));
        RaisePropertyChanged(nameof(ActiveScalarAnalysis));
        RaisePropertyChanged(nameof(HasSecondaryScalarChannel));
        RaisePropertyChanged(nameof(AnalysisPanelTitle));
    }

}
