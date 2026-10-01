using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.BandRatio;

internal sealed class BandRatioRunResultHandler : IAlgorithmRunResultHandler
{
    public string AlgorithmId => "band_ratio";

    public AlgorithmRunParameters ParseParameters(AlgorithmRunInputs inputs) => new(
        RatioBands: AlgorithmRunConfiguration.ParseBandRatioBandsOrThrow(
            inputs.NumeratorLowText, inputs.NumeratorHighText, inputs.DenominatorLowText,
            inputs.DenominatorHighText, inputs.SamplingRateHz));

    public Task ApplyAsync(AlgorithmListViewModel catalog, AnalysisRunResponse run, bool dynamic,
        double? preservedDynamicCursorSeconds)
    {
        catalog.SetStructuredPreviewTextForModule("频段功率比由后端按指定分子/分母频段计算；WPF 仅展示后端返回的比值、功率、质量和动态窗口证据。");
        if (dynamic)
        {
            var preview = BandRatioResultPreview.Parse(run.ResultSummary);
            var points = preview is { IsDynamic: true } ? preview.DynamicPoints : [];
            catalog.SetDynamicResultWindows(points.Select(point => new DynamicWindowRow(point.StartSeconds, point.EndSeconds, AlgorithmResultFormatter.FormatWindowStateForDisplay(point.State), AlgorithmResultFormatter.FormatQualityReasonForDisplay(point.Quality), point.Failure)), points.FirstOrDefault(point => point.IsComplete)?.EndSeconds ?? 0);
            if (preservedDynamicCursorSeconds is double cursor) catalog.SeekDynamicPreviewSecondsForModule(cursor);
        }
        return Task.CompletedTask;
    }
}
