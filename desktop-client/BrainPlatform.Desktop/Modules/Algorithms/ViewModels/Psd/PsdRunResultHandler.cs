using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;

internal sealed class PsdRunResultHandler : IAlgorithmRunResultHandler
{
    public string AlgorithmId => "psd";
    public bool UsesDynamicScalarSeries => false;
    public bool SupportsNotch => true;
    public bool SupportsOnDemandWindows =>
        OfflineAnalysisAvailability.IsOnDemandEnabled(AlgorithmId, enabledByDefault: true);

    public bool SupportsFullRunWindowPreview => true;

    public void ApplyWindowResult(AlgorithmListViewModel catalog, DynamicWindowResponse response) =>
        PsdWindowPresenter.Apply(catalog.PsdPreviewState, response);

    public AlgorithmRunParameters ParseParameters(AlgorithmRunInputs inputs) => new(
        FrequencyBand: AlgorithmRunConfiguration.ParseFrequencyBandOrThrow(
            inputs.LowFrequencyText, inputs.HighFrequencyText, inputs.SamplingRateHz),
        NotchHz: AlgorithmRunConfiguration.ParseNotchFrequency(inputs.NotchFrequency));

    public async Task ApplyAsync(AlgorithmListViewModel catalog, AnalysisRunResponse run, bool dynamic,
        double? preservedDynamicCursorSeconds)
    {
        await catalog.LoadStructuredPreviewAsync(run.RunId, dynamic);
        if (dynamic && preservedDynamicCursorSeconds is double cursorSeconds)
            catalog.SeekDynamicPreviewSecondsForModule(cursorSeconds);
    }
}
