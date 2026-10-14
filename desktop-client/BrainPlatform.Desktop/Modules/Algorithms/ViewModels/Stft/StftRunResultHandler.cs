using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;

internal sealed class StftRunResultHandler : IAlgorithmRunResultHandler
{
    public string AlgorithmId => "stft";
    public bool UsesDynamicScalarSeries => false;
    public bool SupportsNotch => true;
    public bool SupportsOnDemandWindows => string.Equals(
        Environment.GetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_STFT"), "1", StringComparison.Ordinal);

    public void ApplyWindowResult(AlgorithmListViewModel catalog, DynamicWindowResponse response) =>
        StftWindowPresenter.Apply(catalog.StftPreviewState, response);

    public AlgorithmRunParameters ParseParameters(AlgorithmRunInputs inputs)
    {
        if (inputs.Range.EndSeconds - inputs.Range.StartSeconds < 4.0)
            throw new InvalidOperationException("STFT 分析区间至少需要 4 秒。");
        return new(
            FrequencyBand: AlgorithmRunConfiguration.ParseFrequencyBandOrThrow(
                inputs.LowFrequencyText, inputs.HighFrequencyText, inputs.SamplingRateHz),
            NotchHz: AlgorithmRunConfiguration.ParseNotchFrequency(inputs.NotchFrequency));
    }

    public async Task ApplyAsync(AlgorithmListViewModel catalog, AnalysisRunResponse run, bool dynamic,
        double? preservedDynamicCursorSeconds)
    {
        await catalog.LoadStftStructuredPreviewAsync(run.RunId, dynamic);
        if (dynamic && preservedDynamicCursorSeconds is double cursorSeconds)
            catalog.SeekDynamicPreviewSecondsForModule(cursorSeconds);
    }
}
