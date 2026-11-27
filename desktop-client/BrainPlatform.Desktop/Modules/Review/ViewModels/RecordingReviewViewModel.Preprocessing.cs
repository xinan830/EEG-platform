using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Integrates interactive offline scientific preprocessing inspection and downstream algorithm injection into Review.</summary>
public sealed partial class RecordingReviewViewModel
{
    private IAlgorithmClient? preprocessingAlgorithmClient;

    public event EventHandler? OpenPreprocessingRequested;

    public ReviewPreprocessingViewModel Preprocessing { get; private set; } = null!;
    public System.Windows.Input.ICommand OpenPreprocessingCommand { get; private set; } = null!;

    private void InitializePreprocessing(IAlgorithmClient? algorithmClient)
    {
        preprocessingAlgorithmClient = algorithmClient;
        Preprocessing = new ReviewPreprocessingViewModel(
            EnsureBackendRecordingForPreprocessingAsync,
            () => preprocessingAlgorithmClient as IPreprocessingClient,
            seconds => _ = SeekAsync(seconds),
            () => (ViewportStartSeconds, VisibleDurationSeconds, HighPassHz, LowPassHz, NotchHz > 0 ? NotchHz : null),
            () => new PreprocessingRecordingContext(
                RecordingName,
                SourceChannelCount,
                SamplingRateHz,
                DurationSeconds,
                montageSelection.Capture().SelectedMontage?.DisplayName ?? AcquisitionMontage?.DisplayName ?? "原始采集导联",
                HighPassHz,
                LowPassHz,
                NotchHz),
            () => CurrentFrame?.OutputChannelNames ?? []);
        Preprocessing.ConfigurationChanged += OnPreprocessingConfigurationChanged;
        Preprocessing.PropertyChanged += OnPreprocessingPropertyChanged;
        montageSelection.SelectionChanged += OnPreprocessingMontageChanged;
        PropertyChanged += OnPreprocessingViewportChanged;
        OpenPreprocessingCommand = new RelayCommand(() => OpenPreprocessingRequested?.Invoke(this, EventArgs.Empty));
    }

    private void OnPreprocessingPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewPreprocessingViewModel.CurrentOverlayWindow) &&
            Preprocessing.CurrentOverlayWindow is not null && Preprocessing.IsOverlayCompareEnabled)
            _ = Preprocessing.PrefetchAdjacentTilesAsync(DurationSeconds);
    }

    private void OnPreprocessingMontageChanged(object? sender, EventArgs e)
    {
        Preprocessing.RefreshContextMetadata();
        Preprocessing.InvalidateSignalInputs();
    }

    private void OnPreprocessingViewportChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HighPassHz) or nameof(LowPassHz) or nameof(NotchHz))
            Preprocessing.RefreshContextMetadata();

        if (e.PropertyName is nameof(ViewportStartSeconds) or nameof(VisibleDurationSeconds) &&
            Preprocessing.IsOverlayCompareEnabled)
            _ = Preprocessing.HandleViewportChangedAsync(DurationSeconds);
    }

    private void OnPreprocessingConfigurationChanged(object? sender, EventArgs e)
    {
        if (OpenAnalysisSlots.Count > 0)
            InvalidateAnalysisConfiguration();
    }

    private async Task<(string RecordingId, string? MontageId, double DurationSeconds)> EnsureBackendRecordingForPreprocessingAsync(
        CancellationToken cancellationToken)
    {
        if (analysisRecording is null)
        {
            if (analysisRecordingDirectory is null)
                throw new InvalidOperationException("当前回溯记录缺少可用于科学预处理的本地目录。");
            var montages = montageSelection.AvailableMontages.ToArray();
            var coordinator = analysisCoordinators.Values.FirstOrDefault()
                ?? throw new InvalidOperationException("当前回放页未配置后端算法协调器。");
            analysisRecording = await coordinator.RegisterAsync(analysisRecordingDirectory, cancellationToken, montages);
        }

        var selection = montageSelection.Capture();
        var montage = ResolveAnalysisMontage(analysisRecording, selection.SelectedMontage);
        if (selection.SelectedMontage is not null && montage is null)
            throw new ReviewInputMappingException("montage_snapshot_unavailable",
                "后端没有当前导联快照，科学对比不可用。", "montage");
        return (analysisRecording.Id, montage?.Id, analysisRecording.DurationSeconds ?? DurationSeconds);
    }

    private JsonElement AttachActivePreprocessing(JsonElement config, AlgorithmCatalogItem algorithm)
    {
        var preprocessing = Preprocessing.BuildAlgorithmConfigDictionary(algorithm.SupportsArtifactRemoval);
        if (preprocessing is null || config.ValueKind != JsonValueKind.Object)
            return config;

        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in config.EnumerateObject())
            merged[prop.Name] = prop.Value;
        merged["preprocessing"] = preprocessing;
        return JsonSerializer.SerializeToElement(merged);
    }

    private async Task<(RegisteredRecording Recording, HistoricalMontageDescriptor? Montage)> ResolvePreprocessingAnalysisInputsAsync(
        RegisteredRecording recording, HistoricalMontageDescriptor? montage, AlgorithmCatalogItem algorithm, CancellationToken token)
    {
        var payload = Preprocessing.BuildPayload(algorithm.SupportsArtifactRemoval);
        if (preprocessingAlgorithmClient is not IPreprocessingClient client) return (recording, montage);
        var resolved = await client.GetPreprocessingAnalysisChannelsAsync(recording.Id, payload, montage?.Id, token);
        token.ThrowIfCancellationRequested();
        if (resolved.RecordingId != recording.Id || resolved.Channels.Count == 0)
            throw new InvalidOperationException("当前预处理配置没有可用的 EEG 分析通道。");
        var available = resolved.Channels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (recording with { Channels = resolved.Channels }, montage is null ? null : montage with
        { Outputs = montage.Outputs.Where(output => available.Contains(output.Id)).ToArray() });
    }

    private void DisposePreprocessing()
    {
        if (Preprocessing is null) return;
        Preprocessing.ConfigurationChanged -= OnPreprocessingConfigurationChanged;
        Preprocessing.PropertyChanged -= OnPreprocessingPropertyChanged;
        montageSelection.SelectionChanged -= OnPreprocessingMontageChanged;
        PropertyChanged -= OnPreprocessingViewportChanged;
        Preprocessing.Dispose();
    }
}
