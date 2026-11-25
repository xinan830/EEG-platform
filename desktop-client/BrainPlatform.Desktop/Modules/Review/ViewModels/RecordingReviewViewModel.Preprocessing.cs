using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Integrates interactive offline scientific preprocessing inspection and downstream algorithm injection into Review.</summary>
public sealed partial class RecordingReviewViewModel
{
    private const string PreprocessingPanelId = "preprocessing";
    private IAlgorithmClient? preprocessingAlgorithmClient;

    public ReviewPreprocessingViewModel Preprocessing { get; private set; } = null!;
    public System.Windows.Input.ICommand OpenPreprocessingCommand { get; private set; } = null!;
    public bool IsPreprocessingPanelOpen => activeAnalysisId == PreprocessingPanelId;

    private void InitializePreprocessing(IAlgorithmClient? algorithmClient)
    {
        preprocessingAlgorithmClient = algorithmClient;
        Preprocessing = new ReviewPreprocessingViewModel(
            EnsureBackendRecordingForPreprocessingAsync,
            () => preprocessingAlgorithmClient as IPreprocessingClient,
            seconds => _ = SeekAsync(seconds),
            () => (ViewportStartSeconds, VisibleDurationSeconds, HighPassHz, LowPassHz, NotchHz > 0 ? NotchHz : null));
        Preprocessing.ConfigurationChanged += OnPreprocessingConfigurationChanged;
        OpenPreprocessingCommand = new RelayCommand(TogglePreprocessingPanel);
    }

    private void TogglePreprocessingPanel()
    {
        if (IsPreprocessingPanelOpen)
            _ = CloseAnalysisAsync();
        else
            SetActiveAnalysis(PreprocessingPanelId);
    }

    private void OnPreprocessingConfigurationChanged(object? sender, EventArgs e)
    {
        if (activeAnalysisId is not null && !IsPreprocessingPanelOpen)
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
        return (analysisRecording.Id, montage?.Id, analysisRecording.DurationSeconds ?? DurationSeconds);
    }

    private JsonElement AttachActivePreprocessing(JsonElement config, string algorithmId)
    {
        var allowArtifactRemoval = algorithmId is not ("stft" or "tnk_biofeedback");
        var preprocessing = Preprocessing.BuildAlgorithmConfigDictionary(allowArtifactRemoval);
        if (preprocessing is null || config.ValueKind != JsonValueKind.Object)
            return config;

        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in config.EnumerateObject())
            merged[prop.Name] = prop.Value;
        merged["preprocessing"] = preprocessing;
        return JsonSerializer.SerializeToElement(merged);
    }

    private void DisposePreprocessing()
    {
        if (Preprocessing is null) return;
        Preprocessing.ConfigurationChanged -= OnPreprocessingConfigurationChanged;
        Preprocessing.Dispose();
    }
}
