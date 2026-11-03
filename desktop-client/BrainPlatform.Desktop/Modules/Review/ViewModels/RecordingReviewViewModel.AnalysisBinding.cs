using System.Diagnostics;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Review cursor binding and presentation ownership, shared by panel algorithms.</summary>
public sealed partial class RecordingReviewViewModel
{
    private CancellationTokenSource? topomapCancellation;
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
        topomapCancellation?.Cancel();
        topomapCancellation?.Dispose();
        topomapCancellation = null;
        topomap3DCancellation?.Cancel();
        topomap3DCancellation?.Dispose();
        topomap3DCancellation = null;
        RbpAnalysis.ClearTopomap();
        RbpAnalysis.ClearTopomap3D();
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
            if (activeAnalysisId == "rbp" && RbpAnalysis.IsAnyTopomapSelected)
            {
                if (RbpAnalysis.Is3DTopomapSelected) _ = RefreshRbpTopomap3DAsync();
                else _ = RefreshRbpTopomapAsync();
            }
        }
        if (e.PropertyName == nameof(ReviewAnalysisBinding.StatusText))
            AnalysisPreparationText = CurrentAnalysisWindow is null ? AnalysisStatusText : "";
        RaiseAnalysisPropertiesChanged();
    }

    private async Task RefreshRbpTopomapAsync()
    {
        topomap3DCancellation?.Cancel();
        topomapCancellation?.Cancel();
        topomapCancellation?.Dispose();
        topomapCancellation = new CancellationTokenSource();
        var token = topomapCancellation.Token;
        var window = CurrentAnalysisWindow;
        if (window is null || analysisCoordinator is null)
        {
            RbpAnalysis.ClearTopomap();
            return;
        }
        try
        {
            var loadTimer = Stopwatch.StartNew();
            var bandId = string.IsNullOrEmpty(RbpAnalysis.SelectedTopomapBandId)
                ? null
                : RbpAnalysis.SelectedTopomapBandId;
            var result = await analysisCoordinator.LoadTopomapAsync(
                window, token, RbpAnalysis.SelectedTopomapPowerType, bandId);
            if (!token.IsCancellationRequested && RbpAnalysis.ApplyTopomapIfAvailable(result))
            {
                var bindTimer = Stopwatch.StartNew();
                if (DesktopLog.PerformanceDiagnosticsEnabled)
                {
                    var gridCells = result?.Bands.Sum(band =>
                        (long)(band.Grid?.Width ?? 0) * (band.Grid?.Height ?? 0)) ?? 0;
                    DesktopLog.Info(
                        $"review_topomap_perf stage=bind" +
                        $" window_id={result?.RunId ?? "none"}" +
                        $" cursor_sample={window.CursorSample}" +
                        $" bands={result?.Bands.Count ?? 0}" +
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
            if (RbpAnalysis.Is3DTopomapSelected) _ = RefreshRbpTopomap3DAsync();
            else if (RbpAnalysis.IsTopomapSelected) _ = RefreshRbpTopomapAsync();
            else
            {
                topomapCancellation?.Cancel();
                topomap3DCancellation?.Cancel();
            }
        }
        if (activeAnalysisId == "rbp" && RbpAnalysis.Is3DTopomapSelected &&
            e.PropertyName == nameof(RbpReviewAnalysisViewModel.SelectedTopomap3DBandId))
            _ = RefreshRbpTopomap3DAsync();
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
