using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Pairs the existing PSD/RBP adapters per spectral inspection card.</summary>
public sealed partial class RecordingReviewViewModel
{
    private const string SpectralValuesPanelId = "spectral-values";
    private const string CustomFrequencyInspectionPanelId = "custom-frequency-inspection";
    private CancellationTokenSource? spectralProjectionCancellation;
    private long spectralProjectionRevision;
    private WindowPreviewResponse? projectedPsdWindow;
    private WindowPreviewResponse? projectedRbpWindow;

    public ReviewSpectralValuesViewModel SpectralValues { get; } = new();
    public System.Windows.Input.ICommand OpenSpectralValuesCommand { get; private set; } = null!;
    public bool IsSpectralValuesPanelOpen => ActiveAnalysisPanelKind == ReviewAnalysisPanelKind.SpectralValues;
    public bool IsSpectralInspectionPanelOpen => IsSpectralValuesPanelOpen || IsCustomFrequencyInspectionPanelOpen;

    private void InitializeSpectralValues() => OpenSpectralValuesCommand =
        new AsyncRelayCommand(() => OpenAnalysisAsync(SpectralValuesPanelId), ReportCommandError);

    private void AttachSpectralRbp(ReviewAnalysisCardViewModel slot, AlgorithmCatalogItem algorithm,
        WorkspaceMontageSelectionSnapshot<MontageProfile> selection, HistoricalMontageDescriptor? montage)
    {
        var recording = analysisRecording!;
        if (slot.AlgorithmId == SpectralValuesPanelId) SpectralValues.InitializeChannels(PsdParameters.Channels);
        if (slot.AlgorithmId == CustomFrequencyInspectionPanelId) CustomFrequencyInspection.InitializeChannels(PsdParameters.Channels);
        var config = analysisPanels["rbp"].BuildConfigurationForSelection(
            recording.DurationSeconds ?? DurationSeconds, recording.SamplingRateHz ?? samplingRateHz, selection, montage);
        config = AttachActivePreprocessing(config, algorithm);
        if (RbpParameters.WindowSeconds != PsdParameters.WindowSeconds || RbpParameters.StepSeconds != PsdParameters.StepSeconds)
            throw new InvalidOperationException("现有 PSD/RBP 目录窗口设置不一致，不能合并数值。");
        if (!RbpParameters.ResolveAnalysisChannels(montage).SequenceEqual(PsdParameters.Channels, StringComparer.Ordinal))
            throw new InvalidOperationException("现有 PSD/RBP 导联顺序不一致，不能合并数值。");
        var rbpCoordinator = CreateSlotCoordinator("rbp");
        var rbpBinding = new ReviewAnalysisBinding(rbpCoordinator, recording.Id,
            samplingRateHz, playback.DurationSamples, recording);
        slot.SpectralRbpBinding = rbpBinding;
        rbpBinding.AttachOnDemand(new AnalysisRunRequest(recording.Id, "official_algorithm", config), algorithm);
    }

    private async Task RefreshSpectralValuesAsync(ReviewAnalysisCardViewModel slot)
    {
        if (slot.IsClosed || slot.AlgorithmId != SpectralValuesPanelId) return;
        var psd = slot.Binding?.CurrentWindow;
        var rbp = slot.SpectralRbpBinding?.CurrentWindow;
        if (isTimelineDragging || psd is null || rbp is null)
        {
            CancelSpectralProjection();
            SpectralValues.Clear($"PSD：{slot.Binding?.StatusText ?? "准备中"}；RBP：{slot.SpectralRbpBinding?.StatusText ?? "准备中"}");
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
            var psdProvider = slot.Coordinator.GetCapability<IReviewScalarProvider>() ??
                throw new InvalidOperationException("PSD adapter 不提供现有频段投影。");
            var rbpProvider = (slot.SpectralRbpBinding?.Coordinator ?? analysisCoordinators["rbp"])
                .GetCapability<IReviewScalarProvider>() ??
                throw new InvalidOperationException("RBP adapter 不提供振幅投影。");
            var prepared = CurrentPreparedSpectralWindow(slot, psd, rbp);
            if (prepared?.ProjectionError is { } error) throw error;
            var density = prepared?.Density is { } savedDensity ? Task.FromResult(savedDensity) :
                psdProvider.LoadScalarsAsync(psd, request.Token,
                ReviewSpectralSources.Bands.Select(band => $"psd.band-mean-density.{band}").ToArray());
            var amplitude = prepared?.Amplitude is { } savedAmplitude ? Task.FromResult(savedAmplitude) :
                rbpProvider.LoadScalarsAsync(rbp, request.Token,
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
        IsSpectralValuesSlotOpen && !isTimelineDragging && revision == spectralProjectionRevision &&
        ReferenceEquals(psd, projectedPsdWindow) && ReferenceEquals(rbp, projectedRbpWindow);

    private void CancelSpectralProjection()
    {
        spectralProjectionRevision++;
        spectralProjectionCancellation?.Cancel();
        spectralProjectionCancellation = null;
        projectedPsdWindow = projectedRbpWindow = null;
    }

    private void ClearSpectralValuesSlot(ReviewAnalysisCardViewModel slot)
    {
        CancelSpectralWindowLoad(slot);
        CancelSpectralProjection();
        SpectralValues.Clear("等待当前 PSD/RBP 窗口。");
    }

    private void InvalidateSpectralSlot(ReviewAnalysisCardViewModel slot)
    {
        CancelSpectralWindowLoad(slot);
        if (slot.AlgorithmId == SpectralValuesPanelId)
        {
            CancelSpectralProjection();
            SpectralValues.Clear("等待当前 PSD/RBP 窗口。");
        }
        if (slot.AlgorithmId == CustomFrequencyInspectionPanelId)
        {
            CancelCustomFrequencyProjection();
            CustomFrequencyInspection.Clear("等待当前 PSD/RBP 窗口。");
        }
        slot.SpectralRbpBinding?.DetachRun();
    }

    private async ValueTask DetachSpectralSlotAsync(ReviewAnalysisCardViewModel slot)
    {
        CancelSpectralWindowLoad(slot);
        if (slot.AlgorithmId == SpectralValuesPanelId)
        {
            CancelSpectralProjection();
            SpectralValues.Clear("等待当前 PSD/RBP 窗口。");
        }
        if (slot.AlgorithmId == CustomFrequencyInspectionPanelId)
        {
            CancelCustomFrequencyProjection();
            CustomFrequencyInspection.Clear("等待当前 PSD/RBP 窗口。");
        }
        var binding = slot.SpectralRbpBinding;
        slot.SpectralRbpBinding = null;
        if (binding is not null) await binding.DisposeAsync();
    }
}
