using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.ThetaBeta;

internal sealed class ThetaBetaRunResultHandler : IAlgorithmRunResultHandler
{
    public string AlgorithmId => "theta_beta";

    public Task ApplyAsync(AlgorithmListViewModel catalog, AnalysisRunResponse run, bool dynamic,
        double? preservedDynamicCursorSeconds)
    {
        catalog.SetStructuredPreviewTextForModule("Theta/Beta 比值由后端按 IAPF 个体化频段计算；WPF 仅展示后端返回的比值、频段、功率、质量和动态窗口证据。");
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
        var preview = ThetaBetaResultPreview.Parse(run.ResultSummary);
        var points = preview is { IsDynamic: true } ? preview.DynamicPoints : [];
        var rows = points.Select(point => new DynamicWindowRow(
            point.StartSeconds,
            point.EndSeconds,
            AlgorithmResultFormatter.FormatWindowStateForDisplay(point.State),
            AlgorithmResultFormatter.FormatQualityReasonForDisplay(point.Quality),
            point.Failure));
        catalog.SetDynamicResultWindows(rows, points.FirstOrDefault(point => point.IsComplete)?.EndSeconds ?? 0);
    }
}
