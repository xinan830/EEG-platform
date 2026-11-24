using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;

internal sealed class RbpRunResultHandler : IAlgorithmRunResultHandler
{
    public string AlgorithmId => "rbp";
    public bool SupportsNotch => true;
    public bool SupportsOnDemandWindows =>
        OfflineAnalysisAvailability.IsOnDemandEnabled(AlgorithmId, enabledByDefault: true);
    public bool SupportsFullRunWindowPreview => true;
    // RBP is channel-matrix structured output; the generic metric adapter
    // cannot preserve channel order or channel-level failures.
    public FullRunMetricWindowSpec? FullRunMetricWindowSpec => null;

    public void ApplyWindowResult(AlgorithmListViewModel catalog, DynamicWindowResponse response) =>
        _ = RbpResultPreview.FromWindow(response);

    public Task ApplyAsync(AlgorithmListViewModel catalog, AnalysisRunResponse run, bool dynamic,
        double? preservedDynamicCursorSeconds)
    {
        if (dynamic)
            ApplyDynamicWindows(catalog, run);
        var description = "RBP 为 1–50 Hz 五频段结果，各导联独立评估质量。伪迹保留计算值并警告，缺失样本或无效总功率不可用；数值由后端返回。";
        catalog.SetStructuredPreviewTextForModule(description);
        return Task.CompletedTask;
    }

    internal static void ApplyDynamicWindows(AlgorithmListViewModel catalog, AnalysisRunResponse run)
    {
        var preview = RbpResultPreview.Parse(run);
        var rows = preview.Windows.Select(window => new DynamicWindowRow(window.StartSeconds, window.EndSeconds,
            AlgorithmResultFormatter.FormatWindowStateForDisplay(window.State),
            AlgorithmResultFormatter.FormatQualityReasonForDisplay(window.Quality),
            string.IsNullOrWhiteSpace(window.Failure) ? "" : AlgorithmResultFormatter.FormatFailureCodeForDisplay(window.Failure)));
        var firstAvailable = preview.Windows.FirstOrDefault(window => window.State == "Complete" &&
            window.Shares.Any(share => share is not null))
            ?? preview.Windows.FirstOrDefault(window => window.State == "Partial" &&
                window.Shares.Any(share => share is not null));
        catalog.SetDynamicResultWindows(rows, firstAvailable?.EndSeconds ?? 0);
    }
}
