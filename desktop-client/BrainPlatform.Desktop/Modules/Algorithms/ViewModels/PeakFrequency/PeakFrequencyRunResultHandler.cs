using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.PeakFrequency;

internal sealed class PeakFrequencyRunResultHandler : IAlgorithmRunResultHandler
{
    public string AlgorithmId => "peak_frequency";

    public AlgorithmRunParameters ParseParameters(AlgorithmRunInputs inputs) => new(
        FrequencyBand: AlgorithmRunConfiguration.ParseFrequencyBandOrThrow(
            inputs.LowFrequencyText, inputs.HighFrequencyText, inputs.SamplingRateHz));

    public Task ApplyAsync(AlgorithmListViewModel catalog, AnalysisRunResponse run, bool dynamic,
        double? preservedDynamicCursorSeconds)
    {
        catalog.SetStructuredPreviewTextForModule("频段峰频率为后端标量结果，数值由后端直接返回，未在客户端重算。");
        if (dynamic)
        {
            ApplyDynamicWindows(catalog, run);
            if (preservedDynamicCursorSeconds is double cursorSeconds)
                catalog.SeekDynamicPreviewSecondsForModule(cursorSeconds);
        }
        return Task.CompletedTask;
    }

    internal static void ApplyDynamicWindows(AlgorithmListViewModel catalog, AnalysisRunResponse run)
    {
        var preview = PeakFrequencyResultPreview.Parse(run.ResultSummary);
        var points = preview is { IsDynamic: true } ? preview.DynamicPoints : [];
        var rows = points.Select(point => new DynamicWindowRow(point.StartSeconds, point.EndSeconds,
            AlgorithmResultFormatter.FormatWindowStateForDisplay(point.State),
            AlgorithmResultFormatter.FormatQualityReasonForDisplay(point.Quality), point.Failure));
        catalog.SetDynamicResultWindows(rows, points.FirstOrDefault(point => point.IsComplete)?.EndSeconds ?? 0);
    }
}
