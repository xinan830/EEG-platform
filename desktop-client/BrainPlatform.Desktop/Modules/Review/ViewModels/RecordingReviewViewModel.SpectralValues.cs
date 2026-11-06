using System.ComponentModel;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Pairs the existing PSD/RBP adapters on the single review clock.</summary>
public sealed partial class RecordingReviewViewModel
{
    private const string SpectralValuesPanelId = "spectral-values";
    private ReviewAnalysisBinding? spectralRbpBinding;
    private CancellationTokenSource? spectralProjectionCancellation;
    private long spectralProjectionRevision;
    private WindowPreviewResponse? projectedPsdWindow;
    private WindowPreviewResponse? projectedRbpWindow;

    public ReviewSpectralValuesViewModel SpectralValues { get; } = new();
    public System.Windows.Input.ICommand OpenSpectralValuesCommand { get; private set; } = null!;
    public bool IsSpectralValuesPanelOpen => activeAnalysisId == SpectralValuesPanelId;

    private void InitializeSpectralValues() => OpenSpectralValuesCommand =
        new AsyncRelayCommand(() => OpenAnalysisAsync(SpectralValuesPanelId), ReportCommandError);

    private void AttachSpectralRbp(AlgorithmCatalogItem algorithm,
        WorkspaceMontageSelectionSnapshot<MontageProfile> selection, HistoricalMontageDescriptor? montage)
    {
        var recording = analysisRecording!;
        SpectralValues.InitializeChannels(PsdParameters.Channels);
        var config = analysisPanels["rbp"].BuildConfigurationForSelection(
            recording.DurationSeconds ?? DurationSeconds, recording.SamplingRateHz ?? samplingRateHz, selection, montage);
        if (RbpParameters.WindowSeconds != PsdParameters.WindowSeconds || RbpParameters.StepSeconds != PsdParameters.StepSeconds)
            throw new InvalidOperationException("现有 PSD/RBP 目录窗口设置不一致，不能合并数值。");
        if (!RbpParameters.ResolveAnalysisChannels(montage).SequenceEqual(PsdParameters.Channels, StringComparer.Ordinal))
            throw new InvalidOperationException("现有 PSD/RBP 导联顺序不一致，不能合并数值。");
        spectralRbpBinding = new ReviewAnalysisBinding(analysisCoordinators["rbp"], recording.Id,
            samplingRateHz, playback.DurationSamples, recording);
        spectralRbpBinding.AttachOnDemand(new AnalysisRunRequest(recording.Id, "official_algorithm", config), algorithm);
        spectralRbpBinding.PropertyChanged += OnSpectralBindingChanged;
    }

    private void OnSpectralBindingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsSpectralValuesPanelOpen ||
            (!ReferenceEquals(sender, analysisBinding) && !ReferenceEquals(sender, spectralRbpBinding))) return;
        if (e.PropertyName is nameof(ReviewAnalysisBinding.CurrentWindow) or nameof(ReviewAnalysisBinding.IsLoading) or
            nameof(ReviewAnalysisBinding.TargetCursorSample) or nameof(ReviewAnalysisBinding.StatusText))
            _ = RefreshSpectralValuesAsync();
    }

    private async Task RefreshSpectralValuesAsync()
    {
        if (!IsSpectralValuesPanelOpen) return;
        var psd = analysisBinding?.CurrentWindow;
        var rbp = spectralRbpBinding?.CurrentWindow;
        if (isTimelineDragging || psd is null || rbp is null || analysisBinding?.IsTargetDisplayed != true || spectralRbpBinding?.IsTargetDisplayed != true)
        {
            CancelSpectralProjection();
            SpectralValues.Clear($"PSD：{analysisBinding?.StatusText ?? "准备中"}；RBP：{spectralRbpBinding?.StatusText ?? "准备中"}");
            return;
        }
        if (ReferenceEquals(psd, projectedPsdWindow) && ReferenceEquals(rbp, projectedRbpWindow)) return;
        CancelSpectralProjection();
        projectedPsdWindow = psd;
        projectedRbpWindow = rbp;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(analysisLifetime.Token);
        spectralProjectionCancellation = request;
        var revision = spectralProjectionRevision;
        try
        {
            ReviewSpectralSources.Validate(psd, rbp, PsdParameters.Channels);
            SpectralValues.Clear("正在读取当前窗口的频段投影…");
            var psdProvider = analysisCoordinators["psd"].GetCapability<IReviewScalarProvider>() ??
                throw new InvalidOperationException("PSD adapter 不提供现有频段投影。");
            var rbpProvider = analysisCoordinators["rbp"].GetCapability<IReviewScalarProvider>() ??
                throw new InvalidOperationException("RBP adapter 不提供振幅投影。");
            var density = psdProvider.LoadScalarsAsync(psd, request.Token,
                ReviewSpectralSources.Bands.Select(band => $"psd.band-mean-density.{band}").ToArray());
            var amplitude = rbpProvider.LoadScalarsAsync(rbp, request.Token,
                ReviewSpectralSources.Bands.Select(band => $"rbp.amplitude.{band}").ToArray());
            await Task.WhenAll(density, amplitude);
            if (IsCurrentSpectralProjection(revision, psd, rbp) && !request.IsCancellationRequested)
                SpectralValues.Apply(psd, rbp, await density, await amplitude);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrentSpectralProjection(revision, psd, rbp) && !request.IsCancellationRequested)
                SpectralValues.Clear($"数值不可用：{exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(spectralProjectionCancellation, request)) spectralProjectionCancellation = null;
        }
    }

    private bool IsCurrentSpectralProjection(long revision, WindowPreviewResponse psd, WindowPreviewResponse rbp) =>
        IsSpectralValuesPanelOpen && !isTimelineDragging && revision == spectralProjectionRevision &&
        ReferenceEquals(psd, analysisBinding?.CurrentWindow) && ReferenceEquals(rbp, spectralRbpBinding?.CurrentWindow) &&
        analysisBinding?.IsTargetDisplayed == true && spectralRbpBinding?.IsTargetDisplayed == true;

    private void CancelSpectralProjection()
    {
        spectralProjectionRevision++;
        spectralProjectionCancellation?.Cancel();
        spectralProjectionCancellation = null;
        projectedPsdWindow = projectedRbpWindow = null;
    }

    private void InvalidateSpectralValues()
    {
        CancelSpectralProjection();
        spectralRbpBinding?.DetachRun();
        SpectralValues.Clear("等待当前 PSD/RBP 窗口。");
    }

    private async ValueTask DetachSpectralValuesAsync()
    {
        CancelSpectralProjection();
        var binding = spectralRbpBinding;
        spectralRbpBinding = null;
        SpectralValues.Clear("等待当前 PSD/RBP 窗口。");
        if (binding is not null)
        {
            binding.PropertyChanged -= OnSpectralBindingChanged;
            await binding.DisposeAsync();
        }
    }
}
