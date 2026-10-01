using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;

internal sealed class RbpRunResultHandler : IAlgorithmRunResultHandler
{
    public string AlgorithmId => "rbp";

    public Task ApplyAsync(AlgorithmListViewModel catalog, AnalysisRunResponse run, bool dynamic,
        double? preservedDynamicCursorSeconds)
    {
        if (dynamic)
            ApplyDynamicWindows(catalog, run);
        var description = dynamic
            ? run.ScientificVersion == "official-rbp-v2"
                ? "动态 RBP 每个窗口返回 Delta、Theta、Alpha、Beta、Gamma 相对功率，数值由后端直接返回，未在客户端重算。"
                : "历史动态 RBP 每个窗口返回 Delta、Theta、Alpha、Beta 相对功率，数值由后端直接返回，未在客户端重算。"
            : run.ScientificVersion == "official-rbp-v2"
                ? "RBP 为 1–50 Hz 五频段结果，数值由后端直接返回，未在客户端重算。"
                : "历史 RBP 为 1–30 Hz 四频段结果，数值由后端直接返回，未在客户端重算。";
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
